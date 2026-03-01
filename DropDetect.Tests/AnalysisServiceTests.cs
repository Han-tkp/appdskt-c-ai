using DropDetect.Services;
using System;
using System.Linq;
using Xunit;

namespace DropDetect.Tests;

public class AnalysisServiceTests
{
    private readonly AnalysisService _service;

    public AnalysisServiceTests()
    {
        _service = new AnalysisService();
    }

    [Theory]
    [InlineData(25.0, 0.86)]
    [InlineData(18.0, 0.80)]
    [InlineData(12.0, 0.75)]
    [InlineData(8.0, 0.70)]
    public void CalculateSpreadFactor_ReturnsCorrectFactor(double craterDiameter, double expectedSF)
    {
        var sf = _service.CalculateSpreadFactor(craterDiameter);
        Assert.Equal(expectedSF, sf);
    }

    [Fact]
    public void CalculateStatistics_EmptyInput_ReturnsZeroedResult()
    {
        var result = _service.CalculateStatistics(Array.Empty<double>());
        
        Assert.Equal(0, result.Count);
        Assert.False(result.IsPassed);
        Assert.False(result.IsCountSufficient);
    }

    [Fact]
    public void CalculateStatistics_CountUnder200_FlagsSufficientFalse()
    {
        var droplets = Enumerable.Repeat(15.0, 50); // Only 50
        var result = _service.CalculateStatistics(droplets);
        
        Assert.False(result.IsCountSufficient);
        Assert.Equal(50, result.Count);
    }

    [Fact]
    public void CalculateStatistics_CountOver200_FlagsSufficientTrue()
    {
        var droplets = Enumerable.Repeat(15.0, 250); // 250 droplets
        var result = _service.CalculateStatistics(droplets);
        
        Assert.True(result.IsCountSufficient);
        Assert.Equal(250, result.Count);
    }

    [Fact]
    public void CalculateStatistics_ComputesVmdAndSpanCorrectly()
    {
        // Setup raw crater diameters
        double[] craterDiameters = { 12.0, 16.0, 22.0, 18.0, 14.0 };
        // Applied SF:
        // 12.0 -> SF 0.75 -> TrueD = 9.0
        // 14.0 -> SF 0.75 -> TrueD = 10.5
        // 16.0 -> SF 0.80 -> TrueD = 12.8
        // 18.0 -> SF 0.80 -> TrueD = 14.4
        // 22.0 -> SF 0.86 -> TrueD = 18.92
        // Sorted TrueD: 9.0, 10.5, 12.8, 14.4, 18.92

        var result = _service.CalculateStatistics(craterDiameters);

        // Verify count
        Assert.Equal(5, result.Count);

        // VMD should be around the 50% cumulative volume point 
        // 5th true diameter is 18.92, which has a huge volume compared to 9.0.
        // Thus, Dv0.5 should be skewed towards the larger droplets.
        Assert.True(result.Dv05_VMD > 14.0, $"Actual VMD was {result.Dv05_VMD}");
        Assert.True(result.Dv05_VMD < 19.0, $"Actual VMD was {result.Dv05_VMD}");
        
        // Span should be > 0
        Assert.True(result.Span > 0.0);
    }
}
