using Azure.Data.Tables;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class ActivityService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(ILogger<ActivityService> logger)
    {
        var connectionString = Environment.GetEnvironmentVariable("STORAGE")
            ?? throw new InvalidOperationException("STORAGE connection string not configured.");
        _tableServiceClient = new TableServiceClient(connectionString);
        _logger = logger;
    }

    private async Task<TableClient> GetTableAsync()
    {
        var client = _tableServiceClient.GetTableClient(TableNames.ActivityLog);
        await client.CreateIfNotExistsAsync();
        return client;
    }

    public async Task LogAsync(string eventType, string userId, string userDisplayName,
        string description, string? runId = null, string? appName = null)
    {
        try
        {
            var table = await GetTableAsync();
            var entity = ActivityLogEntity.Create(eventType, userId, userDisplayName, description, runId, appName);
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogInformation("Activity logged: {EventType} by {User} - {Description}", eventType, userDisplayName, description);
        }
        catch (Exception ex)
        {
            // Activity logging should never break the main flow
            _logger.LogWarning(ex, "Failed to log activity: {EventType}", eventType);
        }
    }

    public async Task<List<ActivityLogEntity>> GetRecentAsync(int maxResults = 50, string? eventType = null)
    {
        var table = await GetTableAsync();
        var results = new List<ActivityLogEntity>();

        var now = DateTime.UtcNow;
        var currentMonth = now.ToString("yyyy-MM");
        var previousMonth = now.AddMonths(-1).ToString("yyyy-MM");

        string filter = $"PartitionKey eq '{currentMonth}' or PartitionKey eq '{previousMonth}'";
        if (!string.IsNullOrEmpty(eventType))
        {
            filter = $"({filter}) and EventType eq '{eventType}'";
        }

        await foreach (var entity in table.QueryAsync<ActivityLogEntity>(filter, maxPerPage: maxResults))
        {
            results.Add(entity);
            if (results.Count >= maxResults) break;
        }

        return results;
    }
}
