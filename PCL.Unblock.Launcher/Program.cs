using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;

namespace PclUnblock.Launcher;

internal static class Program
{
    private const string DefaultPclFileName = "Plain Craft Launcher 2.exe";

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine("Usage: PCL-Unlock [--pcl <path>] [--verify-only] [-- <PCL arguments>]");
                Console.WriteLine("       PCL-Unlock --licenses");
                return 0;
            }
            if (options.ShowLicenses)
            {
                Console.WriteLine(ReadEmbeddedText("PclUnblock.ThirdPartyNotices.txt"));
                return 0;
            }
            var launcherDirectory = AppContext.BaseDirectory;
            var payloadDirectory = ExtractPayload();
            var hookPath = Path.Combine(payloadDirectory, "PCL.Unblock.Hook.dll");
            ValidatePayload(payloadDirectory, hookPath);

            var pclPath = Path.GetFullPath(options.PclPath ?? Path.Combine(launcherDirectory, DefaultPclFileName));
            if (!File.Exists(pclPath))
                throw new FileNotFoundException("PCL executable was not found. Use --pcl <path>.", pclPath);

            var sessionDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCL-Unlock", "sessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionDirectory);
            var statusPath = Path.Combine(sessionDirectory, "status.txt");

            var startInfo = new ProcessStartInfo
            {
                FileName = pclPath,
                WorkingDirectory = Path.GetDirectoryName(pclPath)!,
                UseShellExecute = false
            };
            foreach (var argument in options.PclArguments)
                startInfo.ArgumentList.Add(argument);

            var existingHooks = startInfo.Environment.TryGetValue("DOTNET_STARTUP_HOOKS", out var hooks)
                ? hooks
                : null;
            startInfo.Environment["DOTNET_STARTUP_HOOKS"] = string.IsNullOrWhiteSpace(existingHooks)
                ? hookPath
                : existingHooks + Path.PathSeparator + hookPath;
            startInfo.Environment["PCL_UNLOCK_PAYLOAD_DIR"] = payloadDirectory;
            startInfo.Environment["PCL_UNLOCK_STATUS_PATH"] = statusPath;
            if (options.VerifyOnly)
                startInfo.Environment["PCL_UNLOCK_VERIFY_ONLY"] = "1";

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start PCL.");
            var result = WaitForHandshake(process, statusPath, TimeSpan.FromSeconds(15));
            Console.WriteLine(result.Detail);
            if (!result.Success && !process.HasExited)
                process.Kill(entireProcessTree: true);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PCL-Unlock: {ex.Message}");
            return 1;
        }
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new MissingManifestResourceException(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ExtractPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = new[]
        {
            new PayloadResource("PclUnblock.Payload.PCL.Unblock.Hook.dll", "PCL.Unblock.Hook.dll"),
            new PayloadResource("PclUnblock.Payload.PCL.Unblock.Patcher.dll", "PCL.Unblock.Patcher.dll"),
            new PayloadResource("PclUnblock.Payload.0Harmony.dll", "0Harmony.dll")
        };
        var payloads = resources.Select(resource =>
        {
            using var stream = assembly.GetManifestResourceStream(resource.ResourceName)
                               ?? throw new MissingManifestResourceException(resource.ResourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return (resource, bytes: memory.ToArray());
        }).ToArray();

        using var combinedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (resource, bytes) in payloads)
        {
            combinedHash.AppendData(System.Text.Encoding.UTF8.GetBytes(resource.FileName));
            combinedHash.AppendData(bytes);
        }
        var version = Convert.ToHexString(combinedHash.GetHashAndReset())[..16];
        var payloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCL-Unlock", "payload", version);
        Directory.CreateDirectory(payloadDirectory);

        foreach (var (resource, bytes) in payloads)
        {
            var targetPath = Path.Combine(payloadDirectory, resource.FileName);
            var expectedHash = SHA256.HashData(bytes);
            if (File.Exists(targetPath) &&
                CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(File.ReadAllBytes(targetPath))))
                continue;

            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        return payloadDirectory;
    }

    private static void ValidatePayload(string payloadDirectory, string hookPath)
    {
        foreach (var path in new[]
                 {
                     hookPath,
                     Path.Combine(payloadDirectory, "PCL.Unblock.Patcher.dll"),
                     Path.Combine(payloadDirectory, "0Harmony.dll")
                 })
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Required payload is missing.", path);
        }
    }

    private static HandshakeResult WaitForHandshake(Process process, string statusPath, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(statusPath))
            {
                var lines = ReadStatusWithRetry(statusPath);
                var success = lines.Length > 0 && lines[0] == "READY";
                var detail = lines.Length > 1 ? string.Join(Environment.NewLine, lines[1..]) : lines[0];
                return new(success, detail);
            }
            if (process.HasExited)
                return new(false, $"PCL exited before the hook handshake (exit code {process.ExitCode}).");
            Thread.Sleep(50);
        }
        return new(false, "Timed out waiting for the startup hook handshake.");
    }

    private static string[] ReadStatusWithRetry(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { return File.ReadAllLines(path); }
            catch (IOException) { Thread.Sleep(25); }
        }
        return File.ReadAllLines(path);
    }

    private sealed record HandshakeResult(bool Success, string Detail);

    private sealed record PayloadResource(string ResourceName, string FileName);

    private sealed record Options(
        string? PclPath,
        bool VerifyOnly,
        bool ShowHelp,
        bool ShowLicenses,
        IReadOnlyList<string> PclArguments)
    {
        public static Options Parse(string[] args)
        {
            string? pclPath = null;
            var verifyOnly = false;
            var showHelp = false;
            var showLicenses = false;
            var pclArguments = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--pcl" when index + 1 < args.Length:
                        pclPath = args[++index];
                        break;
                    case "--verify-only":
                        verifyOnly = true;
                        break;
                    case "--help" or "-h":
                        showHelp = true;
                        break;
                    case "--licenses":
                        showLicenses = true;
                        break;
                    case "--":
                        pclArguments.AddRange(args[(index + 1)..]);
                        index = args.Length;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[index]}");
                }
            }
            return new(pclPath, verifyOnly, showHelp, showLicenses, pclArguments);
        }
    }
}
