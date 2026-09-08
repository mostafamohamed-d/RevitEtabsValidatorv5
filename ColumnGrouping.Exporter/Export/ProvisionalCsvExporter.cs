using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ColumnGrouping.Core.Models;

namespace ColumnGrouping.Exporter.Export
{
    /// <summary>
    /// Provisional export only. The final Excel template/layout is intentionally not assumed.
    /// </summary>
    public sealed class ProvisionalCsvExporter
    {
        public void Export(GroupingReport report, string outputFolder)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);
            WriteColumns(report, Path.Combine(outputFolder, "ColumnGroups_PhysicalColumns.csv"));
            WriteLevels(report, Path.Combine(outputFolder, "ColumnGroups_LevelProfiles.csv"));
            WriteContinuity(report, Path.Combine(outputFolder, "ColumnGroups_Continuity.csv"));
        }

        private static void WriteColumns(GroupingReport report, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PhysicalColumnId,GroupName,SegmentCount,LevelCount,LevelSet,Warnings");
            foreach (var c in report.PhysicalColumns)
            {
                string levels = string.Join("|", c.LevelProfiles.Select(p => p.LevelName));
                sb.AppendLine(string.Join(",",
                    Csv(c.PhysicalColumnId), Csv(c.GroupName), c.Segments.Count.ToString(CultureInfo.InvariantCulture),
                    c.LevelProfiles.Count.ToString(CultureInfo.InvariantCulture), Csv(levels), Csv(string.Join(" || ", c.ContinuityWarnings))));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteLevels(GroupingReport report, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PhysicalColumnId,GroupName,Level,WidthMm,DepthMm,RftRatioDecimal,AsRequiredMm2,BarsWidth,BarsLength,TotalBars,RequiredAreaPerBarMm2,SelectedDiameterMm,SelectedBarAreaMm2,AsProvidedMm2");
            foreach (var c in report.PhysicalColumns)
            foreach (var p in c.LevelProfiles)
            {
                var r = p.Reinforcement;
                sb.AppendLine(string.Join(",", Csv(c.PhysicalColumnId), Csv(c.GroupName), Csv(p.LevelName),
                    Num(p.WidthMm), Num(p.DepthMm), Num(p.RftRatioDecimal), Num(r.RequiredSteelAreaMm2),
                    r.BarsAlongWidth.ToString(CultureInfo.InvariantCulture), r.BarsAlongLength.ToString(CultureInfo.InvariantCulture),
                    r.TotalBars.ToString(CultureInfo.InvariantCulture), Num(r.RequiredAreaPerBarMm2), Num(r.SelectedBarDiameterMm),
                    Num(r.SelectedBarAreaMm2), Num(r.ProvidedSteelAreaMm2)));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteContinuity(GroupingReport report, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PhysicalColumnId,LowerSegmentId,UpperSegmentId,ShiftMm,Status,Message");
            foreach (var c in report.PhysicalColumns)
            foreach (var link in c.ContinuityLinks)
            {
                sb.AppendLine(string.Join(",", Csv(c.PhysicalColumnId), Csv(link.LowerSegmentId), Csv(link.UpperSegmentId),
                    Num(link.ShiftMm), Csv(link.Status.ToString()), Csv(link.Message)));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
