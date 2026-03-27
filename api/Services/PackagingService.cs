using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class PackagingService
{
    private const int DefaultTimeoutSeconds = 300;
    private static readonly char[] DangerousPathChars = { ';', '|', '`', '$', '&' };
    private readonly MetadataReader _metadataReader;
    private readonly StorageService _storageService;
    private readonly IntuneGraphService _intuneService;
    private readonly ILogger<PackagingService> _logger;

    public PackagingService(
        MetadataReader metadataReader,
        StorageService storageService,
        IntuneGraphService intuneService,
        ILogger<PackagingService> logger)
    {
        _metadataReader = metadataReader;
        _storageService = storageService;
        _intuneService = intuneService;
        _logger = logger;
    }

    public async Task<(PackagingRunEntity? Run, string? Error)> StartRunAsync(
        string sourceType,
        string releaseFolderPath,
        bool createIntuneApp,
        string? uploadId = null,
        string? createdBy = null,
        string? createdByName = null,
        string? preAssignedRunId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return (null, "sourceType is required.");

        // For local-upload source type, uploadId is required instead of releaseFolderPath
        if (sourceType == "local-upload")
        {
            if (string.IsNullOrWhiteSpace(uploadId))
                return (null, "uploadId is required for local-upload source type.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(releaseFolderPath))
                return (null, "releaseFolderPath is required.");

            // Path security: resolve full path and block command injection characters
            releaseFolderPath = Path.GetFullPath(releaseFolderPath);
            if (releaseFolderPath.IndexOfAny(DangerousPathChars) >= 0)
                return (null, "releaseFolderPath contains invalid characters.");
        }

        // If local-upload, download staged files to a temp directory
        string? tempUploadDir = null;
        if (sourceType == "local-upload" && !string.IsNullOrWhiteSpace(uploadId))
        {
            tempUploadDir = Path.Combine(Path.GetTempPath(), "packaging-uploads", uploadId);
            Directory.CreateDirectory(tempUploadDir);
            try
            {
                await _storageService.DownloadStagingToLocalAsync(uploadId, tempUploadDir);
                releaseFolderPath = tempUploadDir;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download staged files for upload {UploadId}", uploadId);
                return (null, $"Failed to download uploaded files: {ex.Message}");
            }
        }

        try
        {
            return await ExecuteRunAsync(sourceType, releaseFolderPath, createIntuneApp, uploadId, createdBy, createdByName, preAssignedRunId);
        }
        finally
        {
            // Cleanup temp directory for local uploads
            if (tempUploadDir != null && Directory.Exists(tempUploadDir))
            {
                try { Directory.Delete(tempUploadDir, recursive: true); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to cleanup temp upload dir {Dir}", tempUploadDir); }
            }

            // Cleanup blob staging area
            if (!string.IsNullOrWhiteSpace(uploadId))
            {
                try { await _storageService.CleanupStagingAsync(uploadId); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to cleanup staging for {UploadId}", uploadId); }
            }
        }
    }

    private async Task<(PackagingRunEntity? Run, string? Error)> ExecuteRunAsync(
        string sourceType,
        string releaseFolderPath,
        bool createIntuneApp,
        string? uploadId,
        string? createdBy,
        string? createdByName,
        string? preAssignedRunId)
    {
        var (metadata, metadataError) = await _metadataReader.ReadAsync(releaseFolderPath);
        if (metadata is null)
            return (null, metadataError);

        // Concurrency guard: check for existing Running run with same app+version
        var existingRuns = await _storageService.GetRunsAsync(metadata.ApplicationName, maxResults: 10);
        var alreadyRunning = existingRuns.FirstOrDefault(r =>
            r.Version == metadata.ReleaseVersion && r.Status == RunStatus.Running);
        if (alreadyRunning != null)
            return (null, $"A packaging run is already in progress for {metadata.ApplicationName} v{metadata.ReleaseVersion} (run {alreadyRunning.RunId}).");

        var runId = preAssignedRunId ?? Guid.NewGuid().ToString("N")[..12];
        var startTime = Utc.Now;
        var log = new StringBuilder();

        var run = new PackagingRunEntity
        {
            PartitionKey = PackagingRunEntity.NormalizePartitionKey(metadata.ApplicationName),
            RowKey = $"{PackagingRunEntity.SanitizeTableKey(metadata.ReleaseVersion)}-{runId}",
            RunId = runId,
            AppName = metadata.ApplicationName,
            Version = metadata.ReleaseVersion,
            SourceType = sourceType,
            SourceLocation = releaseFolderPath,
            StartTime = startTime,
            Status = RunStatus.Running,
            MetadataFileReference = Path.Combine(releaseFolderPath, "release-metadata.json"),
            CreateIntuneApp = createIntuneApp,
            MetadataSnapshot = JsonSerializer.Serialize(metadata),
            UploadId = uploadId,
            CreatedBy = createdBy,
            CreatedByName = createdByName
        };

        log.AppendLine($"[{startTime:O}] Run {runId} started");
        log.AppendLine($"  App: {metadata.ApplicationName}");
        log.AppendLine($"  Version: {metadata.ReleaseVersion}");
        log.AppendLine($"  AN Number: {metadata.AnNumber}");
        log.AppendLine($"  Source Type: {sourceType}");
        log.AppendLine($"  Folder: {releaseFolderPath}");
        log.AppendLine($"  Installer: {metadata.InstallerType}");
        log.AppendLine($"  Create Intune App: {createIntuneApp}");

        try
        {
            // Save initial run record
            await _storageService.UpsertRunAsync(run);

            log.AppendLine($"[{Utc.Now:O}] Metadata validated successfully");
            log.AppendLine($"[{Utc.Now:O}] Run record created in Table Storage");

            // Run the Win32 Content Prep Tool
            var toolPath = Environment.GetEnvironmentVariable("WIN32_PREP_TOOL_PATH");
            if (string.IsNullOrWhiteSpace(toolPath))
            {
                run.Status = RunStatus.Failed;
                run.ErrorSummary = "WIN32_PREP_TOOL_PATH is not configured.";
                run.EndTime = Utc.Now;
                log.AppendLine($"[{run.EndTime:O}] ERROR: {run.ErrorSummary}");
                log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");
            }
            else
            {
                var timeoutSeconds = GetTimeoutSeconds();
                log.AppendLine($"[{Utc.Now:O}] Starting Win32 Content Prep Tool: {toolPath}");
                log.AppendLine($"  Timeout: {timeoutSeconds}s");

                var (exitCode, stdout, stderr, timedOut) = await RunToolAsync(
                    toolPath, releaseFolderPath, timeoutSeconds);

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    log.AppendLine($"[{Utc.Now:O}] --- Tool stdout ---");
                    log.AppendLine(stdout);
                }
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    log.AppendLine($"[{Utc.Now:O}] --- Tool stderr ---");
                    log.AppendLine(stderr);
                }

                if (timedOut)
                {
                    run.Status = RunStatus.Failed;
                    run.ErrorSummary = $"Win32 Content Prep Tool timed out after {timeoutSeconds} seconds.";
                    run.EndTime = Utc.Now;
                    log.AppendLine($"[{run.EndTime:O}] ERROR: {run.ErrorSummary}");
                    log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");
                }
                else if (exitCode != 0)
                {
                    run.Status = RunStatus.Failed;
                    run.ErrorSummary = $"Win32 Content Prep Tool exited with code {exitCode}.";
                    run.EndTime = Utc.Now;
                    log.AppendLine($"[{run.EndTime:O}] ERROR: {run.ErrorSummary}");
                    log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");
                }
                else
                {
                    log.AppendLine($"[{Utc.Now:O}] Win32 Content Prep Tool completed successfully (exit code 0)");

                    // Look for the .intunewin artifact in the output directory
                    var outputDir = Path.Combine(releaseFolderPath, "output");
                    var artifactPath = FindIntunewinFile(outputDir) ?? FindIntunewinFile(releaseFolderPath);

                    if (artifactPath != null)
                    {
                        log.AppendLine($"[{Utc.Now:O}] Found artifact: {artifactPath}");
                        try
                        {
                            var blobPath = await _storageService.UploadArtifactAsync(
                                metadata.ApplicationName, runId, artifactPath);
                            run.OutputArtifactPath = blobPath;
                            log.AppendLine($"[{Utc.Now:O}] Artifact uploaded to blob: {blobPath}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to upload artifact for run {RunId}", runId);
                            log.AppendLine($"[{Utc.Now:O}] WARNING: Failed to upload artifact: {ex.Message}");
                            run.Status = RunStatus.SucceededWithWarnings;
                            run.ErrorSummary = "Packaging succeeded but artifact upload failed.";
                        }
                    }
                    else
                    {
                        log.AppendLine($"[{Utc.Now:O}] WARNING: No .intunewin artifact found in output");
                        run.Status = RunStatus.SucceededWithWarnings;
                        run.ErrorSummary = "Packaging tool succeeded but no .intunewin artifact was found.";
                    }

                    // Only set Succeeded if status is still Running (no warnings were set)
                    if (run.Status == RunStatus.Running)
                    {
                        run.Status = RunStatus.Succeeded;
                    }
                    run.EndTime = Utc.Now;
                    log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} failed", runId);
            run.Status = RunStatus.Failed;
            run.ErrorSummary = ex.Message;
            run.EndTime = Utc.Now;
            log.AppendLine($"[{run.EndTime:O}] ERROR: {ex.Message}");
            log.AppendLine($"[{run.EndTime:O}] Run completed with status: {run.Status}");
        }

        // Always upload log, even on failure
        try
        {
            var logUrl = await _storageService.UploadLogAsync(metadata.ApplicationName, runId, log.ToString());
            run.LogUrl = logUrl;
            await _storageService.UpsertRunAsync(run);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload log for run {RunId}", runId);
            // Still update run status even if log upload fails
            await _storageService.UpsertRunAsync(run);
        }

        // Attempt Intune app creation if requested, packaging succeeded, and artifact exists
        if (createIntuneApp &&
            (run.Status == RunStatus.Succeeded) &&
            !string.IsNullOrEmpty(run.OutputArtifactPath))
        {
            _logger.LogInformation("Attempting Intune app creation for run {RunId}", runId);
            try
            {
                var (intuneAppId, intuneAppLink, intuneError) =
                    await _intuneService.CreateFromRunAsync(run, metadata);

                if (intuneAppId is not null)
                {
                    run.IntuneAppId = intuneAppId;
                    run.IntuneAppLink = intuneAppLink;
                    await _storageService.UpsertRunAsync(run);
                    _logger.LogInformation("Intune app {IntuneAppId} created for run {RunId}", intuneAppId, runId);
                }
                else
                {
                    _logger.LogWarning("Intune app creation failed for run {RunId}: {Error}", runId, intuneError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Intune app creation threw for run {RunId}", runId);
            }
        }

        _logger.LogInformation("Run {RunId} completed with status {Status}", runId, run.Status);
        return (run, null);
    }

    private static int GetTimeoutSeconds()
    {
        var envValue = Environment.GetEnvironmentVariable("WIN32_PREP_TOOL_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(envValue) && int.TryParse(envValue, out var seconds) && seconds > 0)
            return seconds;
        return DefaultTimeoutSeconds;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr, bool TimedOut)> RunToolAsync(
        string toolPath, string releaseFolderPath, int timeoutSeconds)
    {
        var outputDir = Path.Combine(releaseFolderPath, "output");
        Directory.CreateDirectory(outputDir);

        // Clean stale .intunewin files from previous runs
        foreach (var staleFile in Directory.GetFiles(outputDir, "*.intunewin"))
        {
            try { File.Delete(staleFile); }
            catch (IOException ex) { _logger.LogDebug(ex, "Could not delete stale file {File}", staleFile); }
        }

        var psi = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = $"-c \"{releaseFolderPath}\" -s \"{releaseFolderPath}\" -o \"{outputDir}\" -q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdoutLock) { stdoutBuilder.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderrLock) { stderrBuilder.AppendLine(e.Data); } };

        _logger.LogInformation("Starting process: {ToolPath} with timeout {Timeout}s", toolPath, timeoutSeconds);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Process timed out after {Timeout}s, killing", timeoutSeconds);
            try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to kill timed-out process"); }
            return (-1, stdoutBuilder.ToString(), stderrBuilder.ToString(), true);
        }
    }

    private static string? FindIntunewinFile(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        var files = Directory.GetFiles(directory, "*.intunewin", SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }
}
