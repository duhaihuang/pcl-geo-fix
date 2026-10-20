using System.Reflection;
using HarmonyLib;

namespace PclGeoFix.Patcher;

public static class EntryPoint
{
    private const string PolicyTypeName = "PCL.RestrictedFeaturePolicy";

    public static string Initialize()
    {
        var policyType = AccessTools.TypeByName(PolicyTypeName)
                         ?? throw new TypeLoadException($"Required type not found: {PolicyTypeName}");
        var isRestrictedGetter = AccessTools.PropertyGetter(policyType, "IsRestricted")
                                 ?? throw new MissingMethodException(policyType.FullName, "get_IsRestricted");
        var check = AccessTools.Method(policyType, "Check", [typeof(string), typeof(bool)])
                    ?? throw new MissingMethodException(policyType.FullName, "Check(string, bool)");
        var throwIfRestricted = AccessTools.Method(policyType, "ThrowIfRestricted", [typeof(string)])
                                ?? throw new MissingMethodException(policyType.FullName,
                                    "ThrowIfRestricted(string)");

        var harmony = new Harmony("io.github.pcl-geo-fix.region-policy");
        harmony.Patch(isRestrictedGetter, prefix: Prefix(nameof(Prefixes.AllowIsRestricted)));
        harmony.Patch(check, prefix: Prefix(nameof(Prefixes.AllowCheck)));
        harmony.Patch(throwIfRestricted, prefix: Prefix(nameof(Prefixes.SkipThrowIfRestricted)));

        Verify(isRestrictedGetter, check, throwIfRestricted);
        return $"Patched {policyType.Assembly.GetName().Name}: " +
               "get_IsRestricted, Check, ThrowIfRestricted";
    }

    private static HarmonyMethod Prefix(string name) => new(
        typeof(Prefixes).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(typeof(Prefixes).FullName, name));

    private static void Verify(MethodInfo isRestrictedGetter, MethodInfo check, MethodInfo throwIfRestricted)
    {
        if (isRestrictedGetter.Invoke(null, null) is not false)
            throw new InvalidOperationException("Patched IsRestricted self-test failed.");
        if (check.Invoke(null, ["pcl-geo-fix-self-test", false]) is not true)
            throw new InvalidOperationException("Patched Check self-test failed.");

        try
        {
            throwIfRestricted.Invoke(null, ["pcl-geo-fix-self-test"]);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException("Patched ThrowIfRestricted self-test failed.", ex.InnerException ?? ex);
        }
    }
}

public static class Prefixes
{
    public static bool AllowIsRestricted(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool AllowCheck(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool SkipThrowIfRestricted() => false;
}
