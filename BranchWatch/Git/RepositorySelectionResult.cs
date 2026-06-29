namespace BranchWatch;

public sealed record RepositorySelectionResult(bool Success, string? RepositoryRoot, string? ErrorMessage)
{
    public static RepositorySelectionResult Succeeded(string repositoryRoot) => new(true, repositoryRoot, null);

    public static RepositorySelectionResult Failed(string errorMessage) => new(false, null, errorMessage);
}
