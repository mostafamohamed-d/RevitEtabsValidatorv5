using System.Reflection;
using System.IO;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace RevitEtabsValidator.ETABS;

internal static class EtabsAssemblyResolver
{
    private const string AssemblySimpleName = "ETABSv1";
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

#if NET8_0_OR_GREATER
        AssemblyLoadContext.Default.Resolving += ResolveNet8;
#else
        AppDomain.CurrentDomain.AssemblyResolve += ResolveNetFramework;
#endif
    }

    private static string? FindEtabsApiPath()
    {
#if ETABS22
        const string version = "22";
#elif ETABS21
        const string version = "21";
#else
        return null;
#endif

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Computers and Structures", $"ETABS {version}", "ETABSv1.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Computers and Structures", $"ETABS {version}", "ETABSv1.dll"),
            Path.Combine(AppContext.BaseDirectory, AssemblySimpleName + ".dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

#if NET8_0_OR_GREATER
    private static Assembly? ResolveNet8(AssemblyLoadContext context, AssemblyName name)
    {
        if (!string.Equals(name.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = FindEtabsApiPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }
#else
    private static Assembly? ResolveNetFramework(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        if (!string.Equals(requested.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = FindEtabsApiPath();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Assembly.LoadFrom(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }
#endif
}
