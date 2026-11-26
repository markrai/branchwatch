namespace BranchWatch;

internal sealed record MissingApplicationLaunchItem(ExistingRestoreItem RestoreItem, string? ExecutablePath,
    ExistingRestoreStatus Status, string Detail)
{
    internal ApplicationLaunchSpec? Spec { get; init; }
}

internal sealed record MissingApplicationLaunchPreparation(ExistingRestorePreparation Existing,
    IReadOnlyList<MissingApplicationLaunchItem> Items);

internal sealed record LaunchedProcessIdentity(uint ProcessId, long CreatedFileTime);

internal interface IMissingApplicationLaunchEnvironment
{
    string? CheckLaunchBlock(string path);
    LaunchedProcessIdentity Launch(string path, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation);
    string? CheckLaunchBlock(ApplicationLaunchSpec spec) => CheckLaunchBlock(spec.ExecutablePath);
    LaunchedProcessIdentity Launch(ApplicationLaunchSpec spec, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation) =>
        Launch(spec.ExecutablePath, desktopId, topology, cancellation);
    void OpenFolder(ApplicationLaunchSpec spec, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation) =>
        throw new NotSupportedException("Brokered folder opening is not supported.");
    bool IsRunning(LaunchedProcessIdentity process);
    bool WaitForInventory(CancellationToken cancellation);
}
