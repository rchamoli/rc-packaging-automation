using System.Text;
using Azure.Storage.Blobs;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class AppDataSeeder
{
    private readonly StorageService _storageService;
    private readonly ILogger<AppDataSeeder> _logger;

    public AppDataSeeder(StorageService storageService, ILogger<AppDataSeeder> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<SeedResult> SeedAsync()
    {
        var result = new SeedResult();

        // Seed packaging runs (3-6 runs across at least 2 apps with varied statuses)
        var runs = GetDemoRuns();
        foreach (var run in runs)
        {
            if (run.Status != RunStatus.Running)
            {
                try
                {
                    var logUrl = await _storageService.UploadLogAsync(
                        run.AppName, run.RunId, BuildSampleLog(run));
                    run.LogUrl = logUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload sample log for run {RunId}", run.RunId);
                }
            }

            if (run.Status == RunStatus.Succeeded && run.OutputArtifactPath != null)
            {
                try
                {
                    await UploadPlaceholderArtifact(run);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload sample artifact for run {RunId}", run.RunId);
                }
            }

            await _storageService.UpsertRunAsync(run);
            result.RunsSeeded++;
        }

        // Seed Intune app references (2-4 refs for dependency/supersedence demos)
        var appRefs = GetDemoIntuneAppRefs();
        foreach (var appRef in appRefs)
        {
            await _storageService.UpsertIntuneAppRefAsync(appRef);
            result.AppRefsSeeded++;
        }

        _logger.LogInformation("Seeded {Runs} runs and {Refs} Intune app refs",
            result.RunsSeeded, result.AppRefsSeeded);

        return result;
    }

    private static List<PackagingRunEntity> GetDemoRuns()
    {
        return new List<PackagingRunEntity>
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
                CreateIntuneApp = true,
                IntuneAppId = "00000000-aaaa-1111-bbbb-000000000001"
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
                CreateIntuneApp = false,
                IntuneAppId = "00000000-aaaa-1111-bbbb-000000000002"
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
                PartitionKey = PackagingRunEntity.NormalizePartitionKey("ChemTracker Desktop"),
                RowKey = "1.5.2-e5f6a1b2c3d4",
                RunId = "e5f6a1b2c3d4",
                AppName = "ChemTracker Desktop",
                Version = "1.5.2",
                SourceType = "Azure Blob Storage",
                SourceLocation = "https://storageacct.blob.core.windows.net/releases/chemtracker/1.5.2",
                StartTime = DateTime.UtcNow.AddDays(-7),
                EndTime = DateTime.UtcNow.AddDays(-7).AddMinutes(8),
                Status = RunStatus.Succeeded,
                OutputArtifactPath = "chemtracker-desktop/e5f6a1b2c3d4/ChemTrackerDesktop.intunewin",
                MetadataFileReference = "https://storageacct.blob.core.windows.net/releases/chemtracker/1.5.2/release-metadata.json",
                CreateIntuneApp = true,
                IntuneAppId = "00000000-cccc-3333-dddd-000000000001"
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
    }

    private static List<IntuneAppRefEntity> GetDemoIntuneAppRefs()
    {
        return new List<IntuneAppRefEntity>
        {
            IntuneAppRefEntity.Create(
                "Nouryon Safety Suite", "2.1.0",
                "00000000-aaaa-1111-bbbb-000000000001",
                "https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/00000000-aaaa-1111-bbbb-000000000001",
                "a1b2c3d4e5f6"),
            IntuneAppRefEntity.Create(
                "Nouryon Safety Suite", "2.0.0",
                "00000000-aaaa-1111-bbbb-000000000002",
                "https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/00000000-aaaa-1111-bbbb-000000000002",
                "b2c3d4e5f6a1"),
            IntuneAppRefEntity.Create(
                "ChemTracker Desktop", "1.5.2",
                "00000000-cccc-3333-dddd-000000000001",
                "https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/00000000-cccc-3333-dddd-000000000001",
                "e5f6a1b2c3d4"),
        };
    }

    private static string BuildSampleLog(PackagingRunEntity run)
    {
        var endTimestamp = run.EndTime ?? run.StartTime.AddMinutes(5);
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
            log.AppendLine($"[{endTimestamp:O}] ERROR: {run.ErrorSummary ?? "Unknown error"}");
        }
        if (run.Status == RunStatus.Succeeded && run.OutputArtifactPath != null)
        {
            log.AppendLine($"[{run.StartTime.AddSeconds(30):O}] Win32 Content Prep Tool completed successfully (exit code 0)");
            log.AppendLine($"[{run.StartTime.AddSeconds(31):O}] Artifact uploaded to blob: {run.OutputArtifactPath}");
        }
        log.AppendLine($"[{endTimestamp:O}] Run completed with status: {run.Status}");
        return log.ToString();
    }

    private async Task UploadPlaceholderArtifact(PackagingRunEntity run)
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

    public class SeedResult
    {
        public int RunsSeeded { get; set; }
        public int AppRefsSeeded { get; set; }
    }
}
