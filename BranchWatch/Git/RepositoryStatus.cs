namespace BranchWatch;

public sealed record RepositoryStatus(
    string? RepositoryRoot,
    string? GitDirectory,
    string? CommonDirectory,
    string BranchDisplay,
    string? HeadRef,
    string? RefPath,
    string? DetachedHeadSha,
    string? ErrorMessage)
{
    public static RepositoryStatus Empty { get; } = new(null, null, null, "No repository selected", null, null, null, null);

    public static RepositoryStatus FromError(RepositoryInfo repository, string errorMessage)
    {
        return new RepositoryStatus(
            repository.RootPath,
            repository.GitDirectory,
            repository.CommonDirectory,
            "Unable to read branch",
            null,
            null,
            null,
            errorMessage);
    }
}
