namespace BranchWatch;

internal sealed record MissingApplicationLaunchItem(ExistingRestoreItem RestoreItem, string? ExecutablePath,
    ExistingRestoreStatus Status, string Detail);

internal sealed record MissingApplicationLaunchPreparation(ExistingRestorePreparation Existing,
    IReadOnlyList<MissingApplicationLaunchItem> Items);

internal sealed record LaunchedProcessIdentity(uint ProcessId, long CreatedFileTime);

internal interface IMissingApplicationLaunchEnvironment
{
    string? CheckLaunchBlock(string path);
    LaunchedProcessIdentity Launch(string path, Guid desktopId, CurrentDisplayTopology topology, CancellationToken cancellation);
    bool IsRunning(LaunchedProcessIdentity process);
    bool WaitForInventory(CancellationToken cancellation);
}
