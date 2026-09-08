using System;
using System.IO;
using ColumnGrouping.Core.Engine;
using ColumnGrouping.Core.Models;
using ColumnGrouping.Exporter.Etabs;
using ColumnGrouping.Exporter.Export;

namespace ColumnGrouping.Exporter
{
    internal static class Program
    {
        private static int Main()
        {
            var settings = new ColumnGroupingSettings();
            settings.Validate();

            var connection = new EtabsConnection();
            if (!connection.ConnectRunning())
            {
                Console.Error.WriteLine(connection.Message);
                return 2;
            }

            Console.WriteLine(connection.Message);
            if (!connection.SetUnitsKnMmC(out string unitsMessage))
            {
                Console.Error.WriteLine(unitsMessage);
                return 3;
            }
            Console.WriteLine(unitsMessage);

            // IMPORTANT:
            // The exact ETABS design-result field/method for the column reinforcement ratio
            // has not yet been confirmed. Do not invent the API call. Replace this provider
            // after inspecting the existing CTI/spColumn/spWall export implementation.
            var ratioProvider = new UnconfiguredReinforcementRatioProvider();
            var reader = new EtabsColumnSegmentReader(connection.SapModel!, ratioProvider);

            var segments = reader.Read(out var diagnostics);
            foreach (var d in diagnostics)
                Console.WriteLine("[DIAGNOSTIC] " + d);

            if (segments.Count == 0)
            {
                Console.Error.WriteLine("No usable ETABS column segments were extracted. Most likely cause: the reinforcement-ratio provider is not configured yet.");
                return 4;
            }

            var engine = new ColumnGroupingEngine(settings);
            var report = engine.Run(segments);

            string outputFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            new ProvisionalCsvExporter().Export(report, outputFolder);

            Console.WriteLine("Physical columns: " + report.PhysicalColumns.Count);
            Console.WriteLine("Groups: " + report.Groups.Count);
            Console.WriteLine("Report folder: " + outputFolder);
            return 0;
        }
    }
}
