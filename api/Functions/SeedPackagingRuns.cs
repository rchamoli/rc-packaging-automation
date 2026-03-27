using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function;

public class SeedPackagingRuns
{
    private readonly StorageService _storageService;
    private readonly ILogger<SeedPackagingRuns> _logger;

    public SeedPackagingRuns(StorageService storageService, ILogger<SeedPackagingRuns> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    [Function("SeedPackagingRuns")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/seed-packaging-runs")] HttpRequestData req)
    {
        _logger.LogInformation("Seeding packaging run data...");

        var runs = new List<PackagingRunEntity>
        {
            new()
            {
                PartitionKey = PackagingRunEntity.NormalizePartitionKey("Nouryon Safety Suite"),
                RowKey = "2.1.0-a1b2c3d4e5f6",
                RunId = "a1b2c3d4e5f6",
                AppName = "Nouryon Safety Suite",
                Version = "2.1.0",
                SourceType = "File Share",
                SourceLocation = @"\\fileserver\releases\safety-suite\2.1.0",
                StartTime = DateTime.UtcNow.AddHours(-2),
                EndTime = DateTime.UtcNow.AddHours(-1).AddMinutes(-45),
                Status = RunStatus.Succeeded,
                OutputArtifactPath = "nouryon-safety-suite/a1b2c3d4e5f6/NouryonSafetySuite.intunewin",
                MetadataFileReference = @"\\fileserver\releases\safety-suite\2.1.0\release-metadata.json",
                CreateIntuneApp = true
            },
            new()
            {
                PartitionKey = PackagingRunEntity.NormalizePartitionKey("Nouryon Safety Suite"),
                RowKey = "2.0.0-b2c3d4e5f6a1",
                RunId = "b2c3d4e5f6a1",
                AppName = "Nouryon Safety Suite",
                Version = "2.0.0",
                SourceType = "File Share",
                SourceLocation = @"\\fileserver\releases\safety-suite\2.0.0",
                StartTime = DateTime.UtcNow.AddDays(-3),
                EndTime = DateTime.UtcNow.AddDays(-3).AddMinutes(12),
                Status = RunStatus.Succeeded,
                MetadataFileReference = @"\\fileserver\releases\safety-suite\2.0.0\release-metadata.json",
                CreateIntuneApp = false
            },
            new()
            {
                PartitionKey = PackagingRunEntity.NormalizePartitionKey("ChemTracker Desktop"),
                RowKey = "1.5.3-c3d4e5f6a1b2",
                RunId = "c3d4e5f6a1b2",
                AppName = "ChemTracker Desktop",
                Version = "1.5.3",
                SourceType = "Azure Blob Storage",
                SourceLocation = "https://storageacct.blob.core.windows.net/releases/chemtracker/1.5.3",
                StartTime = DateTime.UtcNow.AddHours(-1),
                Status = RunStatus.Running,
                CreateIntuneApp = true
            },
            new()
            {
                PartitionKey = PackagingRunEntity.NormalizePartitionKey("Lab Inventory Manager"),
                RowKey = "3.0.1-d4e5f6a1b2c3",
                RunId = "d4e5f6a1b2c3",
                AppName = "Lab Inventory Manager",
                Version = "3.0.1",
                SourceType = "File Share",
                SourceLocation = @"\\fileserver\releases\lab-inventory\3.0.1",
                StartTime = DateTime.UtcNow.AddDays(-1),
                EndTime = DateTime.UtcNow.AddDays(-1).AddMinutes(5),
                Status = RunStatus.Failed,
                ErrorSummary = "Installer file not found in release folder",
                MetadataFileReference = @"\\fileserver\releases\lab-inventory\3.0.1\release-metadata.json",
                CreateIntuneApp = false
            }
        };

        var seeded = 0;
        foreach (var run in runs)
        {
            // Upload a sample log for completed runs
            if (run.Status != RunStatus.Running)
            {
                var log = new StringBuilder();
                log.AppendLine($"[{run.StartTime:O}] Run {run.RunId} started");
                log.AppendLine($"  App: {run.AppName}");
                log.AppendLine($"  Version: {run.Version}");
                log.AppendLine($"  Source Type: {run.SourceType}");
                log.AppendLine($"  Folder: {run.SourceLocation}");
                log.AppendLine($"  Create Intune App: {run.CreateIntuneApp}");
                log.AppendLine($"[{run.StartTime.AddSeconds(5):O}] Metadata validated successfully");
                log.AppendLine($"[{run.StartTime.AddSeconds(10):O}] Run record created in Table Storage");
                if (run.Status == RunStatus.Failed)
                {
                    log.AppendLine($"[{run.EndTime:O}] ERROR: {run.ErrorSummary ?? "Unknown error"}");
                }
                if (run.Status == RunStatus.Succeeded && run.OutputArtifactPath != null)
                {
                    log.AppendLine($"[{run.StartTime.AddSeconds(30):O}] Win32 Content Prep Tool completed successfully (exit code 0)");
                    log.AppendLine($"[{run.StartTime.AddSeconds(31):O}] Artifact uploaded to blob: {run.OutputArtifactPath}");
                }
                log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");

                try
                {
                    var logUrl = await _storageService.UploadLogAsync(run.AppName, run.RunId, log.ToString());
                    run.LogUrl = logUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload sample log for run {RunId}", run.RunId);
                }
            }

            // Upload a sample artifact blob for succeeded runs with artifact path
            if (run.Status == RunStatus.Succeeded && run.OutputArtifactPath != null)
            {
                try
                {
                    var connectionString = Environment.GetEnvironmentVariable("STORAGE")!;
                    var blobServiceClient = new BlobServiceClient(connectionString);
                    var containerClient = blobServiceClient.GetBlobContainerClient(BlobContainers.Artifacts);
                    await containerClient.CreateIfNotExistsAsync();
                    var blobClient = containerClient.GetBlobClient(run.OutputArtifactPath);
                    var sampleContent = Encoding.UTF8.GetBytes(
                        $"[SAMPLE PLACEHOLDER — not a real .intunewin archive] {run.AppName} v{run.Version}");
                    using var stream = new MemoryStream(sampleContent);
                    await blobClient.UploadAsync(stream, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload sample artifact for run {RunId}", run.RunId);
                }
            }

            await _storageService.UpsertRunAsync(run);
            seeded++;
        }

        _logger.LogInformation("Seeded {Count} packaging runs", seeded);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded, table = TableNames.PackagingRuns });
        return response;
    }
}
