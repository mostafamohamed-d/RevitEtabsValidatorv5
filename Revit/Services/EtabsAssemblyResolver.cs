using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RevitEtabsValidator.Revit.Services;

/// <summary>
/// Resolves the CSI ETABSv1 interop assembly at runtime from the installed ETABS
/// directory or from the add-in directory. This is especially important when the
/// add-in is loaded by Revit Add-in Manager, because the manager may load only the
/// validator DLL and not its sibling ETABSv1.dll automatically.
///
/// The resolver supports the project target pairings:
/// Revit 2024 + ETABS 21 and Revit 2025 + ETABS 22.
/// </summary>
internal static class EtabsAssemblyResolver
{
    private const string AssemblySimpleName = "ETABSv1";
    private static bool _installed;

    // This module initializer is intentional: ETABSv1 is an optional host-side
    // dependency that may not be beside the validator DLL when Add-in Manager loads it.
    // Register the resolver as early as possible so the CLR can locate ETABSv1.dll.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        if (_installed)
            return;

        _installed = true;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        AssemblyName requested;
        try
        {
            requested = new AssemblyName(args.Name);
        }
        catch
        {
            return null;
        }

        if (!string.Equals(requested.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var candidate in CandidatePaths())
        {
            try
            {
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            catch
            {
                // Try the next known ETABS installation path.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldTargets = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                    yieldTargets.Add(fullPath);
            }
            catch
            {
                // Ignore malformed paths and continue.
            }
        }

        Add(Path.Combine(AppContext.BaseDirectory, "ETABSv1.dll"));
        Add(Path.Combine(Environment.CurrentDirectory, "ETABSv1.dll"));

        // Scan every installed "ETABS <version>" folder rather than hardcoding 21/22,
        // so this resolver keeps working when the engineer has ETABS 23, 24, or a
        // future release installed instead. See EtabsInstallationScanner for why any
        // installed version is acceptable here.
        Add(RevitEtabsValidator.ETABS.EtabsInstallationScanner.FindNewestApiDll());

        foreach (var path in yieldTargets)
            yield return path;
    }
}
