using System.Reflection;
using System.Runtime.Loader;

public static class StartupHook
{
    private const string TargetAssemblyName = "Plain Craft Launcher 2";
    private const string PatcherAssemblyName = "PCL.Unblock.Patcher";
    private const string HarmonyAssemblyName = "0Harmony";

    public static void Initialize()
    {
        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.Equals(entryAssemblyName, TargetAssemblyName, StringComparison.Ordinal))
            return;

        try
        {
            var payloadDirectory = RequireEnvironmentVariable("PCL_UNLOCK_PAYLOAD_DIR");
            var statusPath = RequireEnvironmentVariable("PCL_UNLOCK_STATUS_PATH");
            AssemblyLoadContext.Default.Resolving += (_, name) => ResolvePayload(payloadDirectory, name);

            var patcherPath = Path.Combine(payloadDirectory, PatcherAssemblyName + ".dll");
            var patcherAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(patcherPath);
            var entryPoint = patcherAssembly.GetType("PclUnblock.Patcher.EntryPoint", throwOnError: true)!;
            var initialize = entryPoint.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
                             ?? throw new MissingMethodException(entryPoint.FullName, "Initialize");
            var result = initialize.Invoke(null, null)?.ToString() ?? "patched";

            WriteStatus(statusPath, "READY", result);
            if (Environment.GetEnvironmentVariable("PCL_UNLOCK_VERIFY_ONLY") == "1")
                Environment.Exit(0);
        }
        catch (Exception ex)
        {
            var root = ex is TargetInvocationException { InnerException: Exception inner }
                ? inner
                : ex;
            TryWriteFailure(root);
            Environment.FailFast("PCL-Unlock failed before launcher startup.", root);
        }
    }

    private static Assembly? ResolvePayload(string payloadDirectory, AssemblyName name)
    {
        if (!string.Equals(name.Name, PatcherAssemblyName, StringComparison.Ordinal) &&
            !string.Equals(name.Name, HarmonyAssemblyName, StringComparison.Ordinal))
            return null;

        var path = Path.Combine(payloadDirectory, name.Name + ".dll");
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    }

    private static string RequireEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing environment variable: {name}");

    private static void TryWriteFailure(Exception exception)
    {
        try
        {
            var statusPath = Environment.GetEnvironmentVariable("PCL_UNLOCK_STATUS_PATH");
            if (!string.IsNullOrWhiteSpace(statusPath))
                WriteStatus(statusPath, "ERROR", exception.ToString());
        }
        catch
        {
            // The original exception remains the authoritative failure.
        }
    }

    private static void WriteStatus(string path, string state, string detail)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{state}{Environment.NewLine}{detail}");
    }
}
