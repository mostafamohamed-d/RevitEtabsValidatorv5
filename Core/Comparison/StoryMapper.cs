using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RevitEtabsValidator.Core.Models;

namespace RevitEtabsValidator.Core.Comparison;

public enum StoryMatchKind
{
    /// <summary>No ETABS story could be associated with this Revit level.</summary>
    None = 0,

    /// <summary>Matched because the normalized level/story names are the same.</summary>
    Name = 1,

    /// <summary>Matched on elevation because no name match was available.</summary>
    Elevation = 2
}

public sealed class StoryMatch
{
    public string RevitLevel { get; init; } = "";
    public double RevitElevationMm { get; init; }
    public string EtabsStory { get; init; } = "";
    public double EtabsElevationMm { get; init; }
    public StoryMatchKind Kind { get; init; }

    /// <summary>Revit level elevation minus the matched ETABS story elevation, in mm.</summary>
    public double ElevationDeltaMm { get; init; }

    public bool IsMatched => Kind != StoryMatchKind.None && !string.IsNullOrWhiteSpace(EtabsStory);
}

/// <summary>
/// Associates Revit levels with ETABS stories.
///
/// The previous rule was "nearest elevation wins, with no distance limit", which
/// has two failure modes seen on real models:
///
/// 1. It depends entirely on the ETABS story elevation list. When
///    <c>cStory.GetNameList</c>/<c>GetElevation</c> returns nothing (the reader
///    used to swallow the error code), EVERY level maps to nothing - and because
///    the validator then filters the ETABS side down to the mapped stories, every
///    member on both sides is reported Missing. A model that is actually
///    coordinated reads as totally uncoordinated.
/// 2. It is blind to a unit mismatch. If ETABS is not in kN-mm-C, its elevations
///    come back in metres and are ~1000x smaller than the Revit millimetres they
///    are compared against, so "nearest" is meaningless.
///
/// So names are used first (they are what an engineer actually relies on, and
/// they survive both failures above), elevation is used only as the fallback, and
/// a unit-scale mismatch is detected and reported rather than silently mismatched.
/// </summary>
public static class StoryMapper
{
    /// <summary>
    /// Strips the decoration that differs between a Revit level name and the ETABS
    /// story it corresponds to, so "BASEMENT 2 LEVEL (SSL)" and "BASEMENT 2", or
    /// "3RD FLOOR (PODIUM DECK) (SSL)" and "3RD FLOOR (PODIUM)", compare equal.
    /// Parenthesised qualifiers are dropped entirely, which also makes the match
    /// robust to them being spelled differently on the two sides.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var text = name!.ToUpperInvariant();

        // Drop parenthesised qualifiers: "(SSL)", "(PODIUM)", "(PODIUM DECK)".
        var withoutParens = new StringBuilder();
        var depth = 0;
        foreach (var c in text)
        {
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') { if (depth > 0) depth--; }
            else if (depth == 0) withoutParens.Append(c);
        }

        // Everything that is not a letter or digit becomes a separator.
        var cleaned = new StringBuilder();
        foreach (var c in withoutParens.ToString())
            cleaned.Append(char.IsLetterOrDigit(c) ? c : ' ');

        // Structural-naming noise words that appear on one side only.
        var noise = new HashSet<string>(StringComparer.Ordinal)
        {
            "LEVEL", "LEVELS", "LVL", "SSL", "FFL", "TOS", "TOC", "EL", "ELEV", "ELEVATION", "STORY", "STOREY"
        };

        var tokens = cleaned.ToString()
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !noise.Contains(t))
            .ToList();

        return string.Join(" ", tokens);
    }

    /// <summary>
    /// Recovers story elevations from the ETABS members themselves, for when the
    /// Story API yields nothing. Every ETABS frame already carries its story name
    /// (read via <c>GetLabelFromName</c>), so the geometry that was read
    /// successfully is enough to rebuild the story table.
    ///
    /// A beam sits at its story's elevation, so its midpoint Z is used directly. A
    /// column assigned to story N spans from the story below up to story N, so its
    /// TOP is the story elevation. The median is taken rather than the mean so a
    /// few sloped or mis-assigned members cannot drag the value off.
    /// </summary>
    public static Dictionary<string, double> DeriveStoryElevations(IEnumerable<ElementBase> etabsElements)
    {
        var samples = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in etabsElements)
        {
            if (element == null || string.IsNullOrWhiteSpace(element.LevelName))
                continue;

            double z;
            if (element is ColumnElement)
                z = Math.Max(element.StartPoint.Z, element.EndPoint.Z);
            else
                z = element.CenterPoint.Z;

            if (double.IsNaN(z) || double.IsInfinity(z))
                continue;

            if (!samples.TryGetValue(element.LevelName, out var list))
                samples[element.LevelName] = list = new List<double>();
            list.Add(z);
        }

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in samples)
        {
            if (pair.Value.Count == 0)
                continue;
            var ordered = pair.Value.OrderBy(x => x).ToList();
            result[pair.Key] = ordered[ordered.Count / 2];
        }
        return result;
    }

    /// <summary>
    /// Detects that the ETABS elevations are expressed in a different unit from the
    /// Revit millimetres, using only the levels/stories that matched by NAME (so the
    /// comparison is between pairs already known to be the same floor). Returns the
    /// factor the ETABS values must be multiplied by - 1.0 when they already agree,
    /// 1000.0 when ETABS is reporting metres. Returns null when there is not enough
    /// evidence to say, so the caller can report uncertainty instead of guessing.
    /// </summary>
    public static double? DetectEtabsUnitScale(
        IEnumerable<(string RevitLevel, double RevitElevationMm)> levels,
        IReadOnlyDictionary<string, double> etabsStoryElevations)
    {
        var byNormalizedStory = BuildNormalizedStoryLookup(etabsStoryElevations);
        var ratios = new List<double>();

        foreach (var level in levels)
        {
            var key = NormalizeName(level.RevitLevel);
            if (key.Length == 0 || !byNormalizedStory.TryGetValue(key, out var story) || story.Count != 1)
                continue;

            var etabsElevation = etabsStoryElevations[story[0]];

            // Near-datum floors (a ground floor at -100 mm) carry almost no scale
            // information and their ratio is dominated by noise, so they are skipped.
            if (Math.Abs(level.RevitElevationMm) < 1000.0 || Math.Abs(etabsElevation) < 1e-6)
                continue;

            ratios.Add(level.RevitElevationMm / etabsElevation);
        }

        if (ratios.Count == 0)
            return null;

        var median = ratios.OrderBy(x => x).ToList()[ratios.Count / 2];
        if (median > 500.0 && median < 2000.0)
            return 1000.0;
        if (median > 0.5 && median < 2.0)
            return 1.0;
        return null;
    }

    /// <summary>
    /// Maps every Revit level to an ETABS story: by normalized name where possible,
    /// otherwise by nearest elevation within <paramref name="maxElevationDeltaMm"/>.
    /// Each ETABS story is claimed at most once, by its best candidate, so two Revit
    /// levels cannot silently collapse onto the same story.
    /// </summary>
    public static List<StoryMatch> Map(
        IEnumerable<(string RevitLevel, double RevitElevationMm)> levels,
        IReadOnlyDictionary<string, double> etabsStoryElevations,
        double maxElevationDeltaMm = 1500.0,
        double? etabsUnitScale = null)
    {
        var levelList = levels.ToList();
        var scale = etabsUnitScale ?? DetectEtabsUnitScale(levelList, etabsStoryElevations) ?? 1.0;
        var scaled = etabsStoryElevations.ToDictionary(
            x => x.Key,
            x => x.Value * scale,
            StringComparer.OrdinalIgnoreCase);

        var byNormalizedStory = BuildNormalizedStoryLookup(etabsStoryElevations);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<StoryMatch>();
        var pendingElevation = new List<(string RevitLevel, double RevitElevationMm)>();

        // Pass 1 - unambiguous name matches take priority over any elevation logic.
        foreach (var level in levelList)
        {
            var key = NormalizeName(level.RevitLevel);
            if (key.Length > 0 &&
                byNormalizedStory.TryGetValue(key, out var candidates) &&
                candidates.Count == 1 &&
                !claimed.Contains(candidates[0]))
            {
                var story = candidates[0];
                claimed.Add(story);
                results.Add(new StoryMatch
                {
                    RevitLevel = level.RevitLevel,
                    RevitElevationMm = level.RevitElevationMm,
                    EtabsStory = story,
                    EtabsElevationMm = scaled[story],
                    Kind = StoryMatchKind.Name,
                    ElevationDeltaMm = level.RevitElevationMm - scaled[story]
                });
            }
            else
            {
                pendingElevation.Add(level);
            }
        }

        // Pass 2 - nearest remaining story by elevation, but only within tolerance.
        // The unbounded "nearest wins" this replaces would happily pair a basement
        // with a roof when nothing else was left.
        foreach (var level in pendingElevation)
        {
            string best = "";
            var bestDelta = double.MaxValue;
            foreach (var pair in scaled)
            {
                if (claimed.Contains(pair.Key))
                    continue;
                var delta = Math.Abs(pair.Value - level.RevitElevationMm);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = pair.Key;
                }
            }

            if (best.Length > 0 && bestDelta <= maxElevationDeltaMm)
            {
                claimed.Add(best);
                results.Add(new StoryMatch
                {
                    RevitLevel = level.RevitLevel,
                    RevitElevationMm = level.RevitElevationMm,
                    EtabsStory = best,
                    EtabsElevationMm = scaled[best],
                    Kind = StoryMatchKind.Elevation,
                    ElevationDeltaMm = level.RevitElevationMm - scaled[best]
                });
            }
            else
            {
                results.Add(new StoryMatch
                {
                    RevitLevel = level.RevitLevel,
                    RevitElevationMm = level.RevitElevationMm,
                    EtabsStory = "",
                    Kind = StoryMatchKind.None
                });
            }
        }

        // Preserve the caller's level order rather than the two-pass order.
        var order = levelList.Select((x, i) => (x.RevitLevel, i))
            .ToDictionary(x => x.RevitLevel, x => x.i, StringComparer.OrdinalIgnoreCase);
        return results
            .OrderBy(x => order.TryGetValue(x.RevitLevel, out var i) ? i : int.MaxValue)
            .ToList();
    }

    private static Dictionary<string, List<string>> BuildNormalizedStoryLookup(
        IReadOnlyDictionary<string, double> etabsStoryElevations)
    {
        var lookup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var story in etabsStoryElevations.Keys)
        {
            var key = NormalizeName(story);
            if (key.Length == 0)
                continue;
            if (!lookup.TryGetValue(key, out var list))
                lookup[key] = list = new List<string>();
            list.Add(story);
        }
        return lookup;
    }
}
