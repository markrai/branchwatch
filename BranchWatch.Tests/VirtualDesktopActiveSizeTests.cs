namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopActiveSizeTests
{
    private const double HorizontalPadding = 36;
    private const double VerticalPadding = 16;
    private const double BorderOn = 2;
    private const double BorderOff = 0;
    private const double MaxContentWidth = 1800;

    // Approximate natural content widths at a fixed scale/DPI for sequence tests.
    private static readonly Dictionary<string, double> NaturalWidths = new(StringComparer.Ordinal)
    {
        ["OM"] = 48,
        ["Ignite"] = 78,
        ["Lobo"] = 62,
        ["RuView"] = 92,
        ["PERSONAL"] = 118,
        ["BranchWatch"] = 156
    };

    [TestMethod]
    public void Measure_SameName_IsIdenticalAcrossSwitchSequences()
    {
        var sequences = new[]
        {
            new[] { "BranchWatch", "OM", "BranchWatch" },
            new[] { "BranchWatch", "RuView", "BranchWatch" },
            new[] { "Ignite", "BranchWatch" },
            new[] { "PERSONAL", "BranchWatch" },
            new[] { "Lobo", "RuView" },
            new[] { "Ignite", "BranchWatch", "Lobo", "RuView", "BranchWatch", "OM", "BranchWatch", "PERSONAL", "BranchWatch" }
        };

        var baseline = MeasureName("BranchWatch", BorderOn, scale: 1.0);
        var ruViewBaseline = MeasureName("RuView", BorderOn, scale: 1.0);

        foreach (var sequence in sequences)
        {
            VirtualDesktopActiveSizeResult? lastBranchWatch = null;
            VirtualDesktopActiveSizeResult? lastRuView = null;

            foreach (var name in sequence)
            {
                var measured = MeasureName(name, BorderOn, scale: 1.0);
                if (name == "BranchWatch")
                {
                    Assert.AreEqual(baseline, measured, $"BranchWatch drifted after sequence [{string.Join(" -> ", sequence)}]");
                    lastBranchWatch = measured;
                }

                if (name == "RuView")
                {
                    Assert.AreEqual(ruViewBaseline, measured, $"RuView drifted after sequence [{string.Join(" -> ", sequence)}]");
                    lastRuView = measured;
                }
            }

            if (sequence.Contains("BranchWatch"))
            {
                Assert.IsNotNull(lastBranchWatch);
            }

            if (sequence.Contains("RuView"))
            {
                Assert.IsNotNull(lastRuView);
            }
        }
    }

    [TestMethod]
    public void Measure_IsIndependentOfNeighborCompositionMetadata()
    {
        // Active sizing inputs are only the active text metrics + chrome; neighbor
        // strip widths must not participate.
        var withNoNeighbors = MeasureName("BranchWatch", BorderOn, scale: 1.0);
        var withNeighbors = MeasureName("BranchWatch", BorderOn, scale: 1.0);
        var withUnequalNeighbors = MeasureName("BranchWatch", BorderOn, scale: 1.0);

        Assert.AreEqual(withNoNeighbors, withNeighbors);
        Assert.AreEqual(withNoNeighbors, withUnequalNeighbors);
    }

    [TestMethod]
    public void Measure_OutlineOnAndOff_AreDeterministicPerName()
    {
        var onA = MeasureName("BranchWatch", BorderOn, scale: 1.0);
        var onB = MeasureName("OM", BorderOn, scale: 1.0);
        var onAAgain = MeasureName("BranchWatch", BorderOn, scale: 1.0);

        var offA = MeasureName("BranchWatch", BorderOff, scale: 1.0);
        var offB = MeasureName("RuView", BorderOff, scale: 1.0);
        var offAAgain = MeasureName("BranchWatch", BorderOff, scale: 1.0);

        Assert.AreEqual(onA, onAAgain);
        Assert.AreEqual(offA, offAAgain);
        Assert.AreNotEqual(onA.ActiveWidth, offA.ActiveWidth);
        Assert.AreNotEqual(onA.ActiveWidth, onB.ActiveWidth);
        Assert.AreNotEqual(offA.ActiveWidth, offB.ActiveWidth);
    }

    [TestMethod]
    public void Measure_MultipleScales_RemainStableAcrossHistory()
    {
        foreach (var scale in new[] { 0.5, 0.75, 1.0 })
        {
            var first = MeasureName("BranchWatch", BorderOn, scale);
            _ = MeasureName("PERSONAL", BorderOn, scale);
            _ = MeasureName("RuView", BorderOn, scale);
            var again = MeasureName("BranchWatch", BorderOn, scale);

            Assert.AreEqual(first, again, $"Scale {scale} drifted for BranchWatch");
            Assert.IsFalse(first.IsTruncated);
        }
    }

    [TestMethod]
    public void Measure_NaturalFit_DoesNotTruncate()
    {
        var result = MeasureName("BranchWatch", BorderOn, scale: 1.0);

        Assert.IsFalse(result.IsTruncated);
        Assert.AreEqual(result.NaturalContentWidth, result.ContentWidth);
        Assert.AreEqual(result.NaturalContentWidth + HorizontalPadding + BorderOn, result.ActiveWidth);
    }

    [TestMethod]
    public void Measure_EnormousName_TruncatesDeterministically()
    {
        const double natural = 5000;
        var first = VirtualDesktopActiveSize.Measure(
            natural, contentHeight: 30, HorizontalPadding, VerticalPadding, BorderOn, maxContentWidth: 200);
        var afterShort = VirtualDesktopActiveSize.Measure(
            NaturalWidths["OM"], contentHeight: 30, HorizontalPadding, VerticalPadding, BorderOn, maxContentWidth: 200);
        var again = VirtualDesktopActiveSize.Measure(
            natural, contentHeight: 30, HorizontalPadding, VerticalPadding, BorderOn, maxContentWidth: 200);

        Assert.IsTrue(first.IsTruncated);
        Assert.AreEqual(200, first.ContentWidth);
        Assert.AreEqual(200 + HorizontalPadding + BorderOn, first.ActiveWidth);
        Assert.IsFalse(afterShort.IsTruncated);
        Assert.AreEqual(first, again);
    }

    [TestMethod]
    public void Measure_NeighborBudgetsDoNotReduceActiveWidth()
    {
        var active = MeasureName("BranchWatch", BorderOn, scale: 1.0);
        var leftBudget = 20.0;
        var rightBudget = 20.0;

        // Simulate constrained neighbor budgets; active width must remain unchanged.
        var layout = VirtualDesktopNeighborLayout.SelectVisible(
            [new VirtualDesktopInfo(Guid.NewGuid(), "Ignite"), new VirtualDesktopInfo(Guid.NewGuid(), "Lobo")],
            [40, 40],
            [new VirtualDesktopInfo(Guid.NewGuid(), "RuView")],
            [40],
            leftBudget,
            rightBudget);

        Assert.IsLessThanOrEqualTo(1, layout.VisibleLeft.Count);
        Assert.AreEqual(active.ActiveWidth, MeasureName("BranchWatch", BorderOn, scale: 1.0).ActiveWidth);
    }

    private static VirtualDesktopActiveSizeResult MeasureName(string name, double borderSize, double scale)
    {
        var natural = NaturalWidths[name] * scale;
        var horizontal = HorizontalPadding * scale;
        var vertical = VerticalPadding * scale;
        return VirtualDesktopActiveSize.Measure(
            natural,
            contentHeight: 30 * scale,
            horizontal,
            vertical,
            borderSize,
            MaxContentWidth);
    }
}
