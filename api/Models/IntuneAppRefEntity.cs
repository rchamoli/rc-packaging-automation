using Azure;
using Azure.Data.Tables;
using Company.Function.Utilities;

namespace Company.Function.Models;

public class IntuneAppRefEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string AppName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string IntuneAppId { get; set; } = string.Empty;
    public string? IntuneAppLink { get; set; }
    public string? RunId { get; set; }

    private DateTime _createdAt;
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => _createdAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>
    /// Creates an IntuneAppRefEntity with PartitionKey = normalized app name, RowKey = sanitized version.
    /// </summary>
    public static IntuneAppRefEntity Create(string appName, string version, string intuneAppId, string? intuneAppLink, string? runId)
    {
        return new IntuneAppRefEntity
        {
            PartitionKey = PackagingRunEntity.NormalizePartitionKey(appName),
            RowKey = PackagingRunEntity.SanitizeTableKey(version),
            AppName = appName,
            Version = version,
            IntuneAppId = intuneAppId,
            IntuneAppLink = intuneAppLink,
            RunId = runId,
            CreatedAt = Utc.Now
        };
    }
}
