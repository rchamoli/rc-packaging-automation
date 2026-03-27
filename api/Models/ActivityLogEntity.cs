using Azure;
using Azure.Data.Tables;
using Company.Function.Utilities;

namespace Company.Function.Models;

public class ActivityLogEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string EventType { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string? AppName { get; set; }

    private DateTime _occurredAt;
    public DateTime OccurredAt
    {
        get => _occurredAt;
        set => _occurredAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public static ActivityLogEntity Create(string eventType, string userId, string userDisplayName, string description, string? runId = null, string? appName = null)
    {
        var now = Utc.Now;
        var invertedTicks = (DateTime.MaxValue.Ticks - now.Ticks).ToString("D20");
        return new ActivityLogEntity
        {
            PartitionKey = now.ToString("yyyy-MM"),
            RowKey = $"{invertedTicks}-{Guid.NewGuid():N}",
            EventType = eventType,
            UserId = userId,
            UserDisplayName = userDisplayName,
            Description = description,
            RunId = runId,
            AppName = appName,
            OccurredAt = now
        };
    }
}

public static class ActivityEventTypes
{
    public const string RunCreated = "run.created";
    public const string RunCompleted = "run.completed";
    public const string IntuneAppCreated = "intune.created";
    public const string ArtifactDownloaded = "artifact.downloaded";
}
