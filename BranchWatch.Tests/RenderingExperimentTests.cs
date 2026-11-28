namespace BranchWatch.Tests;

[TestClass]
public sealed class RenderingExperimentTests
{
    [TestMethod]
    public void ApplyRenderingExperiment_ExplicitOptInEnablesSoftwareRendering()
    {
        var calls = 0;

        Program.ApplyRenderingExperiment("1", () => calls++);

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("true")]
    [DataRow("software")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    public void ApplyRenderingExperiment_OtherValuesLeaveWpfDefaultUntouched(string? value)
    {
        var calls = 0;

        Program.ApplyRenderingExperiment(value, () => calls++);

        Assert.AreEqual(0, calls);
    }
}
