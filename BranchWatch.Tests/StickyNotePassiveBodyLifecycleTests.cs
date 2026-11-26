namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNotePassiveBodyLifecycleTests
{
    [TestMethod]
    public void Refresh_RequiresVerifiedOwnerEligibleLayoutAndReusesAttachedBody()
    {
        using var fixture = new Fixture();
        var lifecycle = fixture.Lifecycle;
        lifecycle.Refresh(true, true);
        Assert.IsEmpty(fixture.Created);
        lifecycle.Enable();
        lifecycle.Refresh(false, true);
        lifecycle.Refresh(true, false);
        Assert.IsEmpty(fixture.Created);
        lifecycle.Refresh(true, true);
        var body = fixture.Created.Single();
        Assert.IsTrue(lifecycle.IsVisible);
        Assert.AreEqual(1, body.ShowCount);
        Assert.AreEqual(1, fixture.PresentationChanges);
        fixture.Note.Text = "Updated";
        lifecycle.Refresh(true, true);
        lifecycle.UpdateContent();
        Assert.HasCount(1, fixture.Created);
        Assert.AreEqual(1, body.ShowCount);
        Assert.AreEqual("Updated", body.Text);
        Assert.AreEqual(1, body.VerifyCount);
        Assert.AreEqual(2, body.AlignCount);
        Assert.AreEqual(0, body.DisposeCount);
    }

    [TestMethod]
    public void ShowFailure_DisposesBeforeReportingAndRetriesOnlyAfterExplicitReset()
    {
        using var fixture = new Fixture();
        fixture.Configure = body => body.ShowSucceeds = false;
        fixture.Lifecycle.Enable();
        fixture.Lifecycle.Refresh(true, true);
        var rejected = fixture.Created.Single();
        Assert.AreEqual(1, rejected.DisposeCount);
        Assert.IsFalse(fixture.Lifecycle.IsVisible);
        Assert.HasCount(1, fixture.Failures);
        Assert.AreEqual("show", fixture.Failures[0]);
        Assert.AreEqual(0, fixture.PresentationChanges);
        fixture.Lifecycle.Refresh(false, true);
        fixture.Lifecycle.Refresh(true, true);
        Assert.HasCount(1, fixture.Created);
        fixture.Configure = null;
        fixture.Lifecycle.ResetFailure();
        fixture.Lifecycle.Refresh(true, true);
        Assert.HasCount(2, fixture.Created);
        Assert.IsTrue(fixture.Lifecycle.IsVisible);
        Assert.AreEqual(1, rejected.DisposeCount);
    }

    [TestMethod]
    public void AlignmentFailure_RestoresOwnerAndSuppressesFurtherAttempts()
    {
        using var fixture = new Fixture();
        fixture.Lifecycle.Enable();
        fixture.Lifecycle.Refresh(true, true);
        var body = fixture.Created.Single();
        body.AlignSucceeds = false;
        fixture.Lifecycle.Align(true, false);
        Assert.IsTrue(fixture.Lifecycle.IsVisible);
        fixture.Lifecycle.Align(true, true);
        Assert.IsFalse(fixture.Lifecycle.IsVisible);
        Assert.AreEqual(1, body.DisposeCount);
        Assert.AreEqual(2, fixture.PresentationChanges);
        Assert.HasCount(1, fixture.Failures);
        Assert.AreEqual("align", fixture.Failures[0]);
        fixture.Lifecycle.Align(true, true);
        fixture.Lifecycle.Refresh(true, true);
        Assert.HasCount(1, fixture.Created);
    }

    [TestMethod]
    public void AffinityLossAndEligibilityLoss_DetachWithoutLatchingFailure()
    {
        using var fixture = new Fixture();
        var lifecycle = fixture.Lifecycle;
        lifecycle.Enable();
        lifecycle.Refresh(true, true);
        var first = fixture.Created.Single();
        first.Affinity = false;
        lifecycle.Refresh(true, true);
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.IsEmpty(fixture.Failures);
        lifecycle.Refresh(true, true);
        var second = fixture.Created.Last();
        Assert.IsTrue(lifecycle.IsVisible);
        second.AlignSucceeds = false;
        second.Affinity = false;
        lifecycle.Align(true, true);
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, second.DisposeCount);
        Assert.IsEmpty(fixture.Failures);
        lifecycle.Refresh(true, true);
        var third = fixture.Created.Last();
        lifecycle.Refresh(false, true);
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, third.DisposeCount);
        lifecycle.Refresh(true, true);
        Assert.HasCount(4, fixture.Created);
        Assert.IsTrue(lifecycle.IsVisible);
    }

    [TestMethod]
    public void ReentrantRefreshAndRemoval_CannotAttachInvalidatedCandidate()
    {
        using var fixture = new Fixture();
        var lifecycle = fixture.Lifecycle;
        lifecycle.Enable();
        fixture.Configure = body => body.OnShow = () =>
        {
            lifecycle.Refresh(true, true);
            lifecycle.Refresh(false, true);
        };
        lifecycle.Refresh(true, true);
        var rejected = fixture.Created.Single();
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, rejected.DisposeCount);
        Assert.AreEqual(0, fixture.PresentationChanges);
        Assert.IsEmpty(fixture.Failures);
        fixture.Configure = null;
        lifecycle.Refresh(true, true);
        Assert.HasCount(2, fixture.Created);
        Assert.IsTrue(lifecycle.IsVisible);
    }

    [TestMethod]
    public void DisposeDuringShow_IsTerminalAndDisposesCandidateExactlyOnce()
    {
        using var fixture = new Fixture();
        var lifecycle = fixture.Lifecycle;
        lifecycle.Enable();
        fixture.Configure = body => body.OnShow = lifecycle.Dispose;
        lifecycle.Refresh(true, true);
        var body = fixture.Created.Single();
        Assert.IsFalse(lifecycle.IsEnabled);
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, body.DisposeCount);
        Assert.AreEqual(0, fixture.PresentationChanges);
        lifecycle.Dispose();
        lifecycle.Enable();
        lifecycle.ResetFailure();
        lifecycle.Refresh(true, true);
        lifecycle.Align(true, true);
        lifecycle.UpdateContent();
        Assert.HasCount(1, fixture.Created);
        Assert.AreEqual(1, body.DisposeCount);
    }

    [TestMethod]
    public void NativeShowException_ClosesCandidateAndRestoresSafeFallback()
    {
        using var fixture = new Fixture();
        fixture.Configure = body => body.OnShow = () => throw new InvalidOperationException("Synthetic");
        fixture.Lifecycle.Enable();
        fixture.Lifecycle.Refresh(true, true);
        Assert.AreEqual(1, fixture.Created.Single().DisposeCount);
        Assert.IsFalse(fixture.Lifecycle.IsVisible);
        Assert.HasCount(1, fixture.Failures);
        Assert.AreEqual(nameof(InvalidOperationException), fixture.Failures[0]);
        fixture.Lifecycle.Refresh(true, true);
        Assert.HasCount(1, fixture.Created);
    }

    [TestMethod]
    public void DisposeAttachedBody_IsIdempotentAndDoesNotRenderClosingOwner()
    {
        using var fixture = new Fixture();
        var lifecycle = fixture.Lifecycle;
        lifecycle.Enable();
        lifecycle.Refresh(true, true);
        var body = fixture.Created.Single();
        lifecycle.Dispose();
        lifecycle.Dispose();
        Assert.IsFalse(lifecycle.IsVisible);
        Assert.AreEqual(1, body.DisposeCount);
        Assert.AreEqual(1, fixture.PresentationChanges);
        lifecycle.Refresh(true, true);
        Assert.HasCount(1, fixture.Created);
    }

    private sealed class Fixture : IDisposable
    {
        internal StickyNote Note { get; } = new();
        internal List<FakeBody> Created { get; } = [];
        internal List<string?> Failures { get; } = [];
        internal Action<FakeBody>? Configure { get; set; }
        internal int PresentationChanges { get; private set; }
        internal StickyNotePassiveBodyLifecycle Lifecycle { get; }

        internal Fixture()
        {
            Lifecycle = new(Note, () =>
            {
                var body = new FakeBody();
                Configure?.Invoke(body);
                Created.Add(body);
                return body;
            }, () => PresentationChanges++, Failures.Add);
        }

        public void Dispose() => Lifecycle.Dispose();
    }

    private sealed class FakeBody : IStickyNotePassiveBody
    {
        public bool IsVisible { get; private set; }
        public string? LastError { get; private set; }
        internal bool ShowSucceeds { get; set; } = true;
        internal bool AlignSucceeds { get; set; } = true;
        internal bool Affinity { get; set; } = true;
        internal Action? OnShow { get; set; }
        internal int ShowCount { get; private set; }
        internal int AlignCount { get; private set; }
        internal int VerifyCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal string? Text { get; private set; }

        public bool TryShow()
        {
            ShowCount++;
            OnShow?.Invoke();
            IsVisible = ShowSucceeds;
            if (!ShowSucceeds) LastError = "show";
            return ShowSucceeds;
        }

        public bool VerifyAffinity()
        {
            VerifyCount++;
            if (!Affinity) LastError = "affinity";
            return Affinity;
        }

        public bool TryAlign()
        {
            AlignCount++;
            if (!AlignSucceeds) LastError = "align";
            return AlignSucceeds;
        }

        public void UpdateContent(StickyNote note) => Text = note.Text;

        public void Dispose()
        {
            DisposeCount++;
            IsVisible = false;
        }
    }
}
