using System;
using ColumnGrouping.Core.Models;

namespace ColumnGrouping.Core.Engine
{
    public sealed class ReinforcementCalculator
    {
        private readonly ColumnGroupingSettings _settings;

        public ReinforcementCalculator(ColumnGroupingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
        }

        public ReinforcementResult Calculate(double widthMm, double depthMm, double rftRatioDecimal)
        {
            if (widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
            if (depthMm <= 0) throw new ArgumentOutOfRangeException(nameof(depthMm));
            if (rftRatioDecimal < 0) throw new ArgumentOutOfRangeException(nameof(rftRatioDecimal));

            int roundedWidth = RoundToNearest100(widthMm);
            int roundedDepth = RoundToNearest100(depthMm);

            int barsWidth = BarsForRoundedDimension(roundedWidth);
            int barsDepth = BarsForRoundedDimension(roundedDepth);
            int totalBars = 2 * barsDepth + 2 * barsWidth - _settings.CornerBars;

            if (totalBars <= 0)
                throw new InvalidOperationException("Calculated total longitudinal bars must be positive.");

            double grossArea = widthMm * depthMm;
            double asRequired = rftRatioDecimal * grossArea;
            double requiredAreaPerBar = asRequired / totalBars;

            SelectBar(requiredAreaPerBar, out double diameter, out double area);

            return new ReinforcementResult
            {
                GrossAreaMm2 = grossArea,
                RequiredSteelAreaMm2 = asRequired,
                BarsAlongWidth = barsWidth,
                BarsAlongLength = barsDepth,
                TotalBars = totalBars,
                RequiredAreaPerBarMm2 = requiredAreaPerBar,
                SelectedBarDiameterMm = diameter,
                SelectedBarAreaMm2 = area,
                ProvidedSteelAreaMm2 = totalBars * area,
                RoundedWidthMm = roundedWidth,
                RoundedLengthMm = roundedDepth
            };
        }

        private int BarsForRoundedDimension(int roundedDimensionMm)
        {
            int raw;
            if (roundedDimensionMm < 500)
                raw = roundedDimensionMm / 100;
            else
                raw = roundedDimensionMm / 100 - 1;

            return Math.Max(_settings.MinimumBarsPerDimension, raw);
        }

        private static int RoundToNearest100(double dimensionMm)
        {
            // Required rule: >50 rounds up, <50 rounds down, exactly 50 rounds down.
            if (dimensionMm <= 0) throw new ArgumentOutOfRangeException(nameof(dimensionMm));
            double lower = Math.Floor(dimensionMm / 100.0) * 100.0;
            double remainder = dimensionMm - lower;
            return (int)(remainder > 50.0 ? lower + 100.0 : lower);
        }

        private void SelectBar(double requiredArea, out double diameter, out double area)
        {
            for (int i = 0; i < _settings.AllowedBarDiametersMm.Length; i++)
            {
                if (_settings.AllowedBarAreasMm2[i] >= requiredArea)
                {
                    diameter = _settings.AllowedBarDiametersMm[i];
                    area = _settings.AllowedBarAreasMm2[i];
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Required area per bar {requiredArea:F2} mm² exceeds the largest configured bar area " +
                $"{_settings.AllowedBarAreasMm2[_settings.AllowedBarAreasMm2.Length - 1]} mm².");
        }
    }
}
