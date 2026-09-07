using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitEtabsValidator.ETABS;

/// <summary>
/// Locates the installed ETABSv1.dll without hardcoding a specific ETABS version.
/// Scans every "ETABS &lt;version&gt;" folder CSI's installer creates under Program
/// Files / Program Files (x86) and picks the newest one that actually contains
/// ETABSv1.dll, so the same compiled add-in works against ETABS 21, 22, 23, 24, or
/// any future release without a separate build per version. Split out from
/// EtabsAssemblyResolver so the pure folder-scanning/version-picking logic (no
/// Windows-only or ETABSv1 dependency) can be exercised directly by
/// Tests/RevitEtabsValidator.Core.Tests on any platform.
/// </summary>
internal static class EtabsInstallationScanner
{
    private const string ApiFileName = "ETABSv1.dll";

    public static string? FindNewestApiDll()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return FindNewestApiDll(roots, Directory.Exists, SafeEnumerateEtabsDirectories, File.Exists);
    }

    internal static string? FindNewestApiDll(
        IEnumerable<string> programFilesRoots,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> enumerateEtabsDirectories,
        Func<string, bool> fileExists)
    {
        var bestVersion = -1;
        string? bestPath = null;

        foreach (var root in programFilesRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var csiRoot = Path.Combine(root, "Computers and Structures");
            if (!directoryExists(csiRoot))
                continue;

            foreach (var dir in enumerateEtabsDirectories(csiRoot))
            {
                var dll = Path.Combine(dir, ApiFileName);
                if (!fileExists(dll))
                    continue;

                var version = ParseVersion(Path.GetFileName(dir));
                if (version > bestVersion)
                {
                    bestVersion = version;
                    bestPath = dll;
                }
            }
        }

        return bestPath;
    }

    private static IEnumerable<string> SafeEnumerateEtabsDirectories(string csiRoot)
    {
        try
        {
            return Directory.EnumerateDirectories(csiRoot, "ETABS*");
        }
        catch
        {
            // A locked-down or unusual Program Files ACL should not block startup;
            // the caller treats "nothing found" as "not resolvable here".
            return Array.Empty<string>();
        }
    }

    // Folder names look like "ETABS 22" (or occasionally carry extra text such as
    // "ETABS 22 Ultimate"); take the digits so both parse to the same version.
    internal static int ParseVersion(string folderName)
    {
        var digits = new string(folderName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var version) ? version : 0;
    }
}
