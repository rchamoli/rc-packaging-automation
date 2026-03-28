using System.IO.Compression;
using Company.Function.Services;

namespace Company.Function.Endpoints;

public static class UploadEndpoints
{
    private const long MaxTotalSize = 500 * 1024 * 1024; // 500 MB
    private const long MaxFileSize = 250 * 1024 * 1024;  // 250 MB per file
    private const int MaxFileCount = 10;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".zip", ".json"
    };

    public static void MapUploadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/packaging/upload", Upload).DisableAntiforgery();
    }

    private static async Task<IResult> Upload(
        HttpContext context,
        StorageService storageService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("UploadEndpoints");
        logger.LogInformation("POST /api/packaging/upload");

        try
        {
            if (!context.Request.HasFormContentType)
                return Results.BadRequest(new { error = "Content-Type must be multipart/form-data." });

            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var form = await context.Request.ReadFormAsync();
            var files = form.Files;

            if (files.Count == 0)
                return Results.BadRequest(new { error = "No files were uploaded." });

            var uploadedFiles = new List<object>();
            long totalSize = 0;
            bool hasZipRelease = false;

            foreach (var file in files)
            {
                if (uploadedFiles.Count >= MaxFileCount)
                    return Results.BadRequest(new { error = $"Too many files (max {MaxFileCount})." });

                var extension = Path.GetExtension(file.FileName);
                if (!AllowedExtensions.Contains(extension))
                    return Results.BadRequest(new { error = $"File type '{extension}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}." });

                if (file.Length > MaxFileSize)
                    return Results.BadRequest(new { error = $"File '{file.FileName}' exceeds max size of {MaxFileSize / 1024 / 1024} MB." });

                totalSize += file.Length;
                if (totalSize > MaxTotalSize)
                    return Results.BadRequest(new { error = $"Total upload size exceeds {MaxTotalSize / 1024 / 1024} MB." });

                using var stream = file.OpenReadStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                var blobPath = await storageService.UploadStagingFileAsync(uploadId, file.FileName, ms);
                uploadedFiles.Add(new { name = file.FileName, size = file.Length, blobPath });

                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) && files.Count == 1)
                    hasZipRelease = true;
            }

            // If a single ZIP was uploaded (likely a release folder), extract it
            if (hasZipRelease && files.Count == 1)
            {
                try
                {
                    var zipFile = files[0];
                    using var zipStream = zipFile.OpenReadStream();
                    using var ms = new MemoryStream();
                    await zipStream.CopyToAsync(ms);
                    ms.Position = 0;

                    using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        using var entryStream = entry.Open();
                        using var entryMs = new MemoryStream();
                        await entryStream.CopyToAsync(entryMs);
                        entryMs.Position = 0;
                        await storageService.UploadStagingFileAsync(uploadId, entry.Name, entryMs);
                    }
                    logger.LogInformation("Extracted ZIP release folder for upload {UploadId}", uploadId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to extract ZIP for upload {UploadId}; files uploaded as-is", uploadId);
                }
            }

            logger.LogInformation("Upload {UploadId} completed: {Count} files, {Size} bytes total",
                uploadId, uploadedFiles.Count, totalSize);

            return Results.Ok(new { uploadId, files = uploadedFiles });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in Upload");
            return Results.Json(new { error = "An unexpected error occurred during file upload." }, statusCode: 500);
        }
    }
}
