using Company.Function.Models;
using Company.Function.Services;

namespace Company.Function.BackgroundServices;

public class PackagingBackgroundService : BackgroundService
{
    private readonly PackagingJobQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PackagingBackgroundService> _logger;

    public PackagingBackgroundService(
        PackagingJobQueue queue,
        IServiceProvider serviceProvider,
        ILogger<PackagingBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PackagingBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            try
            {
                await ProcessJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background packaging job {RunId} failed", job.RunId);
            }
        }
    }

    private async Task ProcessJobAsync(PackagingJob job)
    {
        var packagingService = _serviceProvider.GetRequiredService<PackagingService>();
        var storageService = _serviceProvider.GetRequiredService<StorageService>();
        var activityService = _serviceProvider.GetRequiredService<ActivityService>();
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();

        try
        {
            var (completedRun, error) = await packagingService.StartRunAsync(
                job.SourceType, job.ReleaseFolderPath, job.CreateIntuneApp,
                job.UploadId, job.UserId, job.UserName, job.RunId);

            // Remove the queued placeholder (real entity has proper PK/RK now)
            try { await storageService.DeleteRunEntityAsync("queued", job.RunId); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete queued placeholder for {RunId}", job.RunId); }

            if (completedRun != null)
            {
                await activityService.LogAsync(
                    ActivityEventTypes.RunCompleted,
                    job.UserId, job.UserName,
                    $"Packaging run for {completedRun.AppName} v{completedRun.Version} completed: {completedRun.Status}",
                    job.RunId, completedRun.AppName);

                await notificationService.NotifyRunCompletedAsync(completedRun);
            }
            else
            {
                job.QueuedRun.Status = RunStatus.Failed;
                job.QueuedRun.ErrorSummary = error ?? "Packaging run failed.";
                job.QueuedRun.EndTime = DateTime.UtcNow;
                await storageService.UpsertRunAsync(job.QueuedRun);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background packaging run {RunId} failed", job.RunId);
            try
            {
                job.QueuedRun.Status = RunStatus.Failed;
                job.QueuedRun.ErrorSummary = $"Unexpected error: {ex.Message}";
                job.QueuedRun.EndTime = DateTime.UtcNow;
                await storageService.UpsertRunAsync(job.QueuedRun);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update run {RunId} status after background error", job.RunId);
            }
        }
    }
}
