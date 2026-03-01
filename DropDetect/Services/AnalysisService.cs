using System;
using System.Collections.Generic;
using System.Linq;

namespace DropDetect.Services;

public class DropletData
{
    public double CraterDiameterUm { get; set; }
    public double TrueDiameterUm { get; set; }
    public double Volume { get; set; }
    public double CumulativeVolume { get; set; }
    public double CumulativeVolumePct { get; set; }
}

public class AnalysisResult
{
    public int Count { get; set; }
    public double Dv01 { get; set; }
    public double Dv05_VMD { get; set; }
    public double Dv09 { get; set; }
    public double Span { get; set; }
    public bool IsPassed { get; set; }
    public bool IsCountSufficient { get; set; }
    public int TotalAccumulatedCount { get; set; } // Tracks real-time unique droplets found all-time
    public double InferenceTimeMs { get; set; } = 0;
    public List<DropletData> Droplets { get; set; } = new();
}

public interface IAnalysisService
{
    AnalysisResult CalculateStatistics(IEnumerable<double> craterDiametersUm);
    double CalculateSpreadFactor(double craterDiameterUm);
    System.Collections.Generic.List<double> DownsampleDroplets(System.Collections.Generic.List<double> allDiametersUm, int targetSize);
}

public class AnalysisService : IAnalysisService
{
    // WHO evaluation limits
    private const double VMD_MIN = 10.0;
    private const double VMD_MAX = 30.0;
    private const double SPAN_MAX = 2.0;
    private const int MIN_COUNT = 200;

    /// <summary>
    /// Applies the Spread Factor for MgO slides
    /// </summary>
    public double CalculateSpreadFactor(double craterDiameterUm)
    {
        if (craterDiameterUm > 20) return 0.86;
        if (craterDiameterUm >= 15) return 0.80;
        if (craterDiameterUm >= 10) return 0.75;
        return 0.70;
    }

    /// <summary>
    /// Calculates the VMD (Dv0.5) and SPAN using Cumulative Volume logic (LINQ)
    /// </summary>
    public AnalysisResult CalculateStatistics(IEnumerable<double> craterDiametersUm)
    {
        var rawList = craterDiametersUm.ToList();

        if (!rawList.Any())
        {
            return new AnalysisResult { Count = 0, IsPassed = false, IsCountSufficient = false };
        }

        // 1. Convert to true diameters and compute individual volume
        var dropletsList = rawList.Select(d =>
        {
            double trueD = d * CalculateSpreadFactor(d);
            double vol = (Math.PI / 6.0) * Math.Pow(trueD, 3);
            return new DropletData
            {
                CraterDiameterUm = d,
                TrueDiameterUm = trueD,
                Volume = vol
            };
        })
        // 2. Sort explicitly by ascending TrueDiameter
        .OrderBy(x => x.TrueDiameterUm)
        .ToList();

        // 3. Compute Cumulative Volumes
        double totalVolume = dropletsList.Sum(x => x.Volume);

        double currentCumVol = 0;
        foreach (var drop in dropletsList)
        {
            currentCumVol += drop.Volume;
            drop.CumulativeVolume = currentCumVol;
            drop.CumulativeVolumePct = totalVolume > 0 ? (currentCumVol / totalVolume) * 100.0 : 0;
        }

        // 4. Interpolate Percentiles (Dv0.1, Dv0.5, Dv0.9)
        double dv01 = InterpolateDiameter(dropletsList, 10.0);
        double dv05 = InterpolateDiameter(dropletsList, 50.0); // VMD
        double dv09 = InterpolateDiameter(dropletsList, 90.0);

        // 5. Calculate SPAN
        double span = dv05 > 0 ? (dv09 - dv01) / dv05 : 0;

        // 6. Evaluate Pass/Fail Condition
        bool isVmdPassed = (dv05 >= VMD_MIN && dv05 <= VMD_MAX);
        bool isSpanPassed = (span <= SPAN_MAX);
        bool isCountSufficient = dropletsList.Count >= MIN_COUNT;

        return new AnalysisResult
        {
            Count = dropletsList.Count,
            Dv01 = dv01,
            Dv05_VMD = dv05,
            Dv09 = dv09,
            Span = span,
            IsPassed = isVmdPassed && isSpanPassed,
            IsCountSufficient = isCountSufficient,
            Droplets = dropletsList // Store the detailed data for Excel Export
        };
    }

    private double InterpolateDiameter(List<DropletData> droplets, double targetPct)
    {
        if (droplets.Count == 0) return 0;
        if (droplets.Count == 1) return droplets[0].TrueDiameterUm;

        // Find the first element that exceeds or equals target percentage
        var upperIdx = droplets.FindIndex(d => d.CumulativeVolumePct >= targetPct);

        if (upperIdx == -1) // All below target? (Shouldn't happen for 0-100)
            return droplets.Last().TrueDiameterUm;

        if (upperIdx == 0) // Target is before the first item
            return droplets[0].TrueDiameterUm;

        var lower = droplets[upperIdx - 1];
        var upper = droplets[upperIdx];

        // Linear interpolation formula:
        // y = y0 + (x - x0) * (y1 - y0) / (x1 - x0)
        // x is CumVolPct, y is Diameter
        double x = targetPct;
        double x0 = lower.CumulativeVolumePct;
        double x1 = upper.CumulativeVolumePct;

        if (Math.Abs(x1 - x0) < 0.0001) return upper.TrueDiameterUm;

        double y0 = lower.TrueDiameterUm;
        double y1 = upper.TrueDiameterUm;

        double y = y0 + (x - x0) * (y1 - y0) / (x1 - x0);
        return y;
    }

    /// <summary>
    /// Uses Stratified Random Sampling across 10 decile bins to reduce a massive scan list down to exactly the targetSize
    /// while mathematically preserving the D10, D50 (VMD), and D90 ratios.
    /// </summary>
    public System.Collections.Generic.List<double> DownsampleDroplets(System.Collections.Generic.List<double> allDiametersUm, int targetSize)
    {
        if (allDiametersUm.Count <= targetSize || targetSize <= 0)
        {
            return allDiametersUm.ToList();
        }

        var sorted = allDiametersUm.OrderBy(x => x).ToList();
        var result = new System.Collections.Generic.List<double>();
        Random rng = new Random();

        int numBins = 10;
        int itemsPerBin = sorted.Count / numBins;
        int targetPerBin = targetSize / numBins;
        int remainder = targetSize % numBins;

        for (int i = 0; i < numBins; i++)
        {
            int startIdx = i * itemsPerBin;
            int count = (i == numBins - 1) ? sorted.Count - startIdx : itemsPerBin;
            var bin = sorted.Skip(startIdx).Take(count).ToList();

            int samplesToTake = targetPerBin + (i < remainder ? 1 : 0);

            var selected = bin.OrderBy(x => rng.Next()).Take(samplesToTake);
            result.AddRange(selected);
        }

        return result.OrderBy(x => rng.Next()).ToList(); // Shuffle final results
    }
}
