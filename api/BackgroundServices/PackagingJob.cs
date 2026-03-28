using Company.Function.Models;

namespace Company.Function.BackgroundServices;

public record PackagingJob
{
    public required string RunId { get; init; }
    public required string SourceType { get; init; }
    public required string ReleaseFolderPath { get; init; }
    public required bool CreateIntuneApp { get; init; }
    public string? UploadId { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required PackagingRunEntity QueuedRun { get; init; }
}
