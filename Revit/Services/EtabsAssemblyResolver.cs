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

    [ModuleInitializer]
    internal static void Initialize()
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

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (seen.Add(Path.GetFullPath(path)))
                    yieldTargets.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Ignore malformed paths and continue.
            }
        }

        var yieldTargets = new List<string>();

        Add(Path.Combine(AppContext.BaseDirectory, "ETABSv1.dll"));
        Add(Path.Combine(Environment.CurrentDirectory, "ETABSv1.dll"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        Add(Path.Combine(programFiles, "Computers and Structures", "ETABS 21", "ETABSv1.dll"));
        Add(Path.Combine(programFiles, "Computers and Structures", "ETABS 22", "ETABSv1.dll"));
        Add(Path.Combine(programFilesX86, "Computers and Structures", "ETABS 21", "ETABSv1.dll"));
        Add(Path.Combine(programFilesX86, "Computers and Structures", "ETABS 22", "ETABSv1.dll"));

        foreach (var path in yieldTargets)
            yield return path;
    }
}
