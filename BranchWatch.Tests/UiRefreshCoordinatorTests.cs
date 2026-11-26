namespace BranchWatch.Tests;

[TestClass]
public sealed class UiRefreshCoordinatorTests
{
    [TestMethod]
    public void Requests_CoalesceAndRenderLatestState()
    {
        var queue = new Queue<Action>();
        var value = 0;
        var seen = new List<int>();
        using var coordinator = new UiRefreshCoordinator(queue.Enqueue, () => seen.Add(value));
        coordinator.Request();
        value = 1;
        coordinator.Request();
        value = 2;
        coordinator.Request();
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        CollectionAssert.AreEqual(new[] { 2 }, seen.ToArray());
        Assert.IsEmpty(queue);
    }

    [TestMethod]
    public void ImmediateRefresh_ConsumesPendingWorkWithoutDroppingLaterRequests()
    {
        var queue = new Queue<Action>();
        var renders = 0;
        using var coordinator = new UiRefreshCoordinator(queue.Enqueue, () => renders++);
        coordinator.Request();
        coordinator.RefreshNow();
        Assert.AreEqual(1, renders);
        queue.Dequeue()();
        Assert.AreEqual(1, renders);

        coordinator.Request();
        coordinator.RefreshNow();
        coordinator.Request();
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        Assert.AreEqual(3, renders);
    }

    [TestMethod]
    public void RequestDuringRender_QueuesAnotherPass()
    {
        var queue = new Queue<Action>();
        var renders = 0;
        UiRefreshCoordinator? coordinator = null;
        using var refresh = new UiRefreshCoordinator(queue.Enqueue, () =>
        {
            if (++renders == 1) coordinator!.Request();
        });
        coordinator = refresh;
        refresh.Request();
        queue.Dequeue()();
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        Assert.AreEqual(2, renders);
    }

    [TestMethod]
    public void Disposal_IgnoresPendingAndFutureWork()
    {
        var queue = new Queue<Action>();
        var renders = 0;
        using var coordinator = new UiRefreshCoordinator(queue.Enqueue, () => renders++);
        coordinator.Request();
        coordinator.Dispose();
        coordinator.Dispose();
        coordinator.Request();
        coordinator.RefreshNow();
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        Assert.AreEqual(0, renders);
        Assert.IsEmpty(queue);
    }

    [TestMethod]
    public void FailedPost_AllowsRetry()
    {
        var queue = new Queue<Action>();
        var fail = true;
        var renders = 0;
        using var coordinator = new UiRefreshCoordinator(action =>
        {
            if (fail) throw new InvalidOperationException();
            queue.Enqueue(action);
        }, () => renders++);
        Assert.ThrowsExactly<InvalidOperationException>(coordinator.Request);
        fail = false;
        coordinator.Request();
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        Assert.AreEqual(1, renders);
    }

    [TestMethod]
    public void ConcurrentRequests_PostExactlyOneCallback()
    {
        var queue = new Queue<Action>();
        var renders = 0;
        using var coordinator = new UiRefreshCoordinator(queue.Enqueue, () => renders++);
        Parallel.For(0, 100, _ => coordinator.Request());
        Assert.HasCount(1, queue);
        queue.Dequeue()();
        Assert.AreEqual(1, renders);
    }
}
