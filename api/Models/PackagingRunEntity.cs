using Azure;
using Azure.Data.Tables;

namespace Company.Function.Models;

public class PackagingRunEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string RunId { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;

    private DateTime _startTime;
    public DateTime StartTime
    {
        get => _startTime;
        set => _startTime = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private DateTime? _endTime;
    public DateTime? EndTime
    {
        get => _endTime;
        set => _endTime = value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
    }
    public string Status { get; set; } = RunStatus.Running;
    public string? LogUrl { get; set; }
    public string? OutputArtifactPath { get; set; }
    public string? ErrorSummary { get; set; }
    public string? MetadataFileReference { get; set; }
    public string? IntuneAppId { get; set; }
    public string? IntuneAppLink { get; set; }
    public bool CreateIntuneApp { get; set; }
    public string? UatGroupId { get; set; }
    public string? MetadataSnapshot { get; set; }
    public string? UploadId { get; set; }
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }

    /// <summary>
    /// Strips characters that are disallowed in Azure Table Storage keys (/, \, #, ?).
    /// </summary>
    public static string SanitizeTableKey(string key) =>
        key.Replace("/", "").Replace("\\", "").Replace("#", "").Replace("?", "");

    public static string NormalizePartitionKey(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return "unknown";
        var normalized = appName.Trim().ToLowerInvariant().Replace(" ", "-");
        return SanitizeTableKey(normalized);
    }
}

public static class RunStatus
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string SucceededWithWarnings = "SucceededWithWarnings";
    public const string Failed = "Failed";
}
