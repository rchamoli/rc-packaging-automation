using Azure;
using Azure.Data.Tables;

namespace Company.Function.Models;

public class NotificationPreferenceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public bool EmailOnSuccess { get; set; } = false;
    public bool EmailOnFailure { get; set; } = true;
    public bool TeamsOnSuccess { get; set; } = true;
    public bool TeamsOnFailure { get; set; } = true;
    public string? EmailAddress { get; set; }
    public string? TeamsWebhookUrl { get; set; }
}
