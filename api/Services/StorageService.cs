using System.Text;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class StorageService
{
    private readonly string _connectionString;
    private readonly TableServiceClient _tableServiceClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<StorageService> _logger;

    public StorageService(ILogger<StorageService> logger)
    {
        _connectionString = Environment.GetEnvironmentVariable("STORAGE")
            ?? throw new InvalidOperationException("STORAGE connection string not configured.");
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _logger = logger;
    }

    public async Task<TableClient> GetRunsTableAsync()
    {
        var client = _tableServiceClient.GetTableClient(TableNames.PackagingRuns);
        await client.CreateIfNotExistsAsync();
        return client;
    }

    public async Task<PackagingStats> GetStatsAsync()
    {
        var table = await GetRunsTableAsync();
        int total = 0, succeeded = 0, failed = 0, running = 0, warnings = 0;
        var recentRuns = new List<PackagingRunEntity>();

        await foreach (var entity in table.QueryAsync<PackagingRunEntity>())
        {
            total++;
            switch (entity.Status)
            {
                case RunStatus.Succeeded: succeeded++; break;
                case RunStatus.Failed: failed++; break;
                case RunStatus.Running: running++; break;
                case RunStatus.SucceededWithWarnings: warnings++; break;
            }
            recentRuns.Add(entity);
        }

        recentRuns.Sort((a, b) => b.StartTime.CompareTo(a.StartTime));

        return new PackagingStats
        {
            TotalRuns = total,
            Succeeded = succeeded,
            Failed = failed,
            Running = running,
            SucceededWithWarnings = warnings,
            SuccessRate = total > 0
                ? Math.Round((double)(succeeded + warnings) / total * 100, 1)
                : 0,
            RecentRuns = recentRuns.Take(10).ToList()
        };
    }

    public async Task<List<PackagingRunEntity>> GetRunsAsync(string? appName = null, int maxResults = 50)
    {
        var table = await GetRunsTableAsync();
        var runs = new List<PackagingRunEntity>();

        // Use server-side OData filter when appName is provided
        string? filter = null;
        if (!string.IsNullOrWhiteSpace(appName))
        {
            var normalizedKey = PackagingRunEntity.NormalizePartitionKey(appName);
            filter = TableClient.CreateQueryFilter($"PartitionKey eq {normalizedKey}");
        }

        await foreach (var entity in table.QueryAsync<PackagingRunEntity>(filter, maxPerPage: maxResults))
        {
            runs.Add(entity);
            if (runs.Count >= maxResults) break;
        }

        // Sort by start time descending (most recent first)
        runs.Sort((a, b) => b.StartTime.CompareTo(a.StartTime));

        _logger.LogInformation("Retrieved {Count} runs (filter: {Filter})", runs.Count, appName ?? "none");
        return runs;
    }

    public async Task UpsertRunAsync(PackagingRunEntity run)
    {
        var table = await GetRunsTableAsync();
        await table.UpsertEntityAsync(run, TableUpdateMode.Replace);
        _logger.LogInformation("Upserted run {RunId} with status {Status}", run.RunId, run.Status);
    }

    public async Task DeleteRunEntityAsync(string partitionKey, string rowKey)
    {
        var table = await GetRunsTableAsync();
        try
        {
            await table.DeleteEntityAsync(partitionKey, rowKey);
            _logger.LogInformation("Deleted run entity {PK}/{RK}", partitionKey, rowKey);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Run entity {PK}/{RK} not found for deletion", partitionKey, rowKey);
        }
    }

    public async Task<PackagingRunEntity?> GetRunByIdAsync(string runId)
    {
        var table = await GetRunsTableAsync();

        // RunId is embedded in the RowKey ({version}-{runId}), so we scan all partitions
        var filter = TableClient.CreateQueryFilter($"RunId eq {runId}");

        await foreach (var entity in table.QueryAsync<PackagingRunEntity>(filter))
        {
            _logger.LogInformation("Found run {RunId} in partition {PartitionKey}", runId, entity.PartitionKey);
            return entity;
        }

        _logger.LogInformation("Run {RunId} not found", runId);
        return null;
    }

    public string? GenerateBlobSasUrl(string containerName, string blobName, int expiryMinutes = 30)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!blobClient.CanGenerateSasUri)
            {
                _logger.LogWarning("Cannot generate SAS URI for blob {BlobName} — account key unavailable", blobName);
                return blobClient.Uri.ToString();
            }

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            _logger.LogInformation("Generated SAS URL for blob {BlobName}", blobName);
            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate SAS URL for blob {BlobName}", blobName);
            return null;
        }
    }

    public async Task<string> UploadLogAsync(string appName, string runId, string logContent)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.PackagingLogs);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = $"{PackagingRunEntity.NormalizePartitionKey(appName)}/{runId}.log";
        var blobClient = containerClient.GetBlobClient(blobName);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(logContent));
        await blobClient.UploadAsync(stream, overwrite: true);

        _logger.LogInformation("Uploaded log blob {BlobName}", blobName);
        // Return SAS URL instead of raw blob URI
        return GenerateBlobSasUrl(BlobContainers.PackagingLogs, blobName) ?? blobClient.Uri.ToString();
    }

    // ── Intune App Ref operations ──────────────────────────────────────

    public async Task<TableClient> GetIntuneAppRefsTableAsync()
    {
        var client = _tableServiceClient.GetTableClient(TableNames.IntuneAppRefs);
        await client.CreateIfNotExistsAsync();
        return client;
    }

    public async Task UpsertIntuneAppRefAsync(IntuneAppRefEntity appRef)
    {
        var table = await GetIntuneAppRefsTableAsync();
        await table.UpsertEntityAsync(appRef, TableUpdateMode.Replace);
        _logger.LogInformation("Upserted Intune app ref {AppName} v{Version} → {IntuneAppId}",
            appRef.AppName, appRef.Version, appRef.IntuneAppId);
    }

    public async Task<IntuneAppRefEntity?> GetIntuneAppRefAsync(string appName, string version)
    {
        var table = await GetIntuneAppRefsTableAsync();
        var partitionKey = PackagingRunEntity.NormalizePartitionKey(appName);

        try
        {
            var entity = await table.GetEntityAsync<IntuneAppRefEntity>(partitionKey, version);
            return entity?.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Intune app ref not found for {AppName} v{Version}", appName, version);
            return null;
        }
    }

    // ── Clear helpers (for demo reset) ────────────────────────────────

    public async Task ClearTableAsync(string tableName)
    {
        var tableClient = _tableServiceClient.GetTableClient(tableName);
        try
        {
            await tableClient.DeleteAsync();
            _logger.LogInformation("Deleted table {Table}", tableName);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Table {Table} does not exist, nothing to delete", tableName);
        }
    }

    public async Task ClearBlobContainerAsync(string containerName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        if (!await containerClient.ExistsAsync())
        {
            _logger.LogInformation("Container {Container} does not exist, nothing to delete", containerName);
            return;
        }

        var blobs = new List<string>();
        await foreach (var blob in containerClient.GetBlobsAsync())
            blobs.Add(blob.Name);

        // Delete in parallel batches of 16
        foreach (var batch in blobs.Chunk(16))
        {
            await Task.WhenAll(batch.Select(name => containerClient.DeleteBlobAsync(name)));
        }
        _logger.LogInformation("Deleted {Count} blobs from container {Container}", blobs.Count, containerName);
    }

    // ── Blob operations ────────────────────────────────────────────────

    public async Task<string> UploadArtifactAsync(string appName, string runId, string localFilePath)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.Artifacts);
        await containerClient.CreateIfNotExistsAsync();

        var fileName = Path.GetFileName(localFilePath);
        var blobName = $"{PackagingRunEntity.NormalizePartitionKey(appName)}/{runId}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        using var stream = File.OpenRead(localFilePath);
        await blobClient.UploadAsync(stream, overwrite: true);

        _logger.LogInformation("Uploaded artifact blob {BlobName}", blobName);
        return blobName;
    }

    // ── Upload staging operations (for local file upload feature) ─────

    public async Task<string> UploadStagingFileAsync(string uploadId, string fileName, Stream content)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.Uploads);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = $"{uploadId}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true);

        _logger.LogInformation("Uploaded staging file {BlobName}", blobName);
        return blobName;
    }

    public async Task DownloadStagingToLocalAsync(string uploadId, string localDir)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.Uploads);
        if (!await containerClient.ExistsAsync())
            throw new InvalidOperationException($"Upload staging container does not exist for upload {uploadId}.");

        var prefix = $"{uploadId}/";
        int downloaded = 0;

        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix))
        {
            var relativePath = blob.Name[prefix.Length..];
            var localPath = Path.Combine(localDir, relativePath);
            var dir = Path.GetDirectoryName(localPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var blobClient = containerClient.GetBlobClient(blob.Name);
            await blobClient.DownloadToAsync(localPath);
            downloaded++;
        }

        _logger.LogInformation("Downloaded {Count} staging files for upload {UploadId}", downloaded, uploadId);
        if (downloaded == 0)
            throw new InvalidOperationException($"No staged files found for upload {uploadId}.");
    }

    public async Task CleanupStagingAsync(string uploadId)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.Uploads);
        if (!await containerClient.ExistsAsync()) return;

        var prefix = $"{uploadId}/";
        var blobs = new List<string>();
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix))
            blobs.Add(blob.Name);

        foreach (var batch in blobs.Chunk(16))
        {
            await Task.WhenAll(batch.Select(name => containerClient.DeleteBlobAsync(name)));
        }
        _logger.LogInformation("Cleaned up {Count} staging blobs for upload {UploadId}", blobs.Count, uploadId);
    }

    public async Task<List<string>> ListStagingFilesAsync(string uploadId)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(BlobContainers.Uploads);
        if (!await containerClient.ExistsAsync()) return new List<string>();

        var prefix = $"{uploadId}/";
        var files = new List<string>();
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix))
            files.Add(blob.Name[prefix.Length..]);
        return files;
    }
}
