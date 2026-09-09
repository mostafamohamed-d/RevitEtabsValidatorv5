using System;
using System.Linq;
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
    private static string? _etabsDirectory;

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

    public static Assembly EnsureEtabsApiLoaded()
    {
        Initialize();

        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase));

        if (alreadyLoaded != null)
            return alreadyLoaded;

        var path = FindInstalledApiPath();
        if (string.IsNullOrWhiteSpace(path))
            throw new FileNotFoundException("ETABSv1.dll was not found in the configured ETABS installation.");

        _etabsDirectory = Path.GetDirectoryName(path);

        try
        {
#if NET8_0_OR_GREATER
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
#else
            return Assembly.LoadFrom(Path.GetFullPath(path));
#endif
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ETABS API was found at '{path}', but .NET could not load it. " +
                "This usually means an ETABS dependency/version conflict. " +
                $"Original error: {ex.Message}", ex);
        }
    }

    // Any ETABS version works here, not just the one this build's target framework
    // was compiled against (ETABS21/ETABS22 only pick the *default* HintPath used to
    // compile against the API surface - see RevitEtabsValidator.csproj). CSI keeps the
    // handful of OAPI members this project calls (FrameObj/PointObj/PropFrame/Story,
    // SetPresentUnits, ApplicationStart) stable release to release, so scanning every
    // installed "ETABS <version>" folder under Program Files and preferring the
    // newest one found lets the add-in run against ETABS 21, 22, 23, 24, or whatever
    // is actually installed on the engineer's machine - without a separate build per
    // ETABS release.
    public static string? FindInstalledApiPath() => EtabsInstallationScanner.FindNewestApiDll();

#if NET8_0_OR_GREATER
    private static Assembly? ResolveNet8(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.Equals(name.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
        {
            var path = FindInstalledApiPath();
            if (string.IsNullOrWhiteSpace(path))
                return null;

            _etabsDirectory = Path.GetDirectoryName(path);
            try
            {
                return context.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            catch
            {
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(_etabsDirectory))
        {
            var dependencyPath = Path.Combine(_etabsDirectory, $"{name.Name}.dll");
            if (File.Exists(dependencyPath))
            {
                try
                {
                    return context.LoadFromAssemblyPath(Path.GetFullPath(dependencyPath));
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }
#else
    private static Assembly? ResolveNetFramework(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);

        if (string.Equals(requested.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
        {
            var path = FindInstalledApiPath();
            if (string.IsNullOrWhiteSpace(path))
                return null;

            _etabsDirectory = Path.GetDirectoryName(path);
            try
            {
                return Assembly.LoadFrom(Path.GetFullPath(path));
            }
            catch
            {
                return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(_etabsDirectory))
        {
            var dependencyPath = Path.Combine(_etabsDirectory, $"{requested.Name}.dll");
            if (File.Exists(dependencyPath))
            {
                try
                {
                    return Assembly.LoadFrom(Path.GetFullPath(dependencyPath));
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }
#endif
}
