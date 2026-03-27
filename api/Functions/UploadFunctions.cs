using System.IO.Compression;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function;

public class UploadFunctions
{
    private const long MaxTotalSize = 500 * 1024 * 1024; // 500 MB
    private const long MaxFileSize = 250 * 1024 * 1024;  // 250 MB per file
    private const int MaxFileCount = 10;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".zip", ".json"
    };

    private readonly StorageService _storageService;
    private readonly ILogger<UploadFunctions> _logger;

    public UploadFunctions(StorageService storageService, ILogger<UploadFunctions> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/packaging/upload
    /// Accepts multipart/form-data file uploads. Stages files in blob storage for later packaging.
    /// </summary>
    [Function("UploadPackagingFiles")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "packaging/upload")] HttpRequestData req)
    {
        _logger.LogInformation("POST /api/packaging/upload");

        try
        {
            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault() ?? string.Empty;

            if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Content-Type must be multipart/form-data." });
                return badReq;
            }

            // Parse the multipart form data
            var boundary = GetBoundary(contentType);
            if (boundary is null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Could not parse multipart boundary." });
                return badReq;
            }

            var uploadedFiles = new List<object>();
            long totalSize = 0;
            int fileCount = 0;
            bool hasZipRelease = false;

            // Read the body into memory for parsing
            using var memStream = new MemoryStream();
            await req.Body.CopyToAsync(memStream);
            memStream.Position = 0;

            var parts = await ParseMultipartAsync(memStream, boundary);

            foreach (var (fileName, fileContent) in parts)
            {
                fileCount++;
                if (fileCount > MaxFileCount)
                {
                    var tooMany = req.CreateResponse(HttpStatusCode.BadRequest);
                    await tooMany.WriteAsJsonAsync(new { error = $"Too many files (max {MaxFileCount})." });
                    return tooMany;
                }

                var extension = Path.GetExtension(fileName);
                if (!AllowedExtensions.Contains(extension))
                {
                    var badExt = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badExt.WriteAsJsonAsync(new { error = $"File type '{extension}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}." });
                    return badExt;
                }

                if (fileContent.Length > MaxFileSize)
                {
                    var tooLarge = req.CreateResponse(HttpStatusCode.BadRequest);
                    await tooLarge.WriteAsJsonAsync(new { error = $"File '{fileName}' exceeds max size of {MaxFileSize / 1024 / 1024} MB." });
                    return tooLarge;
                }

                totalSize += fileContent.Length;
                if (totalSize > MaxTotalSize)
                {
                    var tooLarge = req.CreateResponse(HttpStatusCode.BadRequest);
                    await tooLarge.WriteAsJsonAsync(new { error = $"Total upload size exceeds {MaxTotalSize / 1024 / 1024} MB." });
                    return tooLarge;
                }

                // Upload to staging
                fileContent.Position = 0;
                var blobPath = await _storageService.UploadStagingFileAsync(uploadId, fileName, fileContent);

                uploadedFiles.Add(new { name = fileName, size = fileContent.Length, blobPath });

                // Track if this is a ZIP that could be a release folder
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) && fileCount == 1)
                    hasZipRelease = true;
            }

            if (uploadedFiles.Count == 0)
            {
                var noFiles = req.CreateResponse(HttpStatusCode.BadRequest);
                await noFiles.WriteAsJsonAsync(new { error = "No files were uploaded." });
                return noFiles;
            }

            // If a single ZIP was uploaded (likely a release folder), extract it
            if (hasZipRelease && parts.Count == 1)
            {
                try
                {
                    var (zipName, zipContent) = parts[0];
                    zipContent.Position = 0;
                    await ExtractZipToStagingAsync(uploadId, zipName, zipContent);
                    _logger.LogInformation("Extracted ZIP release folder for upload {UploadId}", uploadId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract ZIP for upload {UploadId}; files uploaded as-is", uploadId);
                }
            }

            _logger.LogInformation("Upload {UploadId} completed: {Count} files, {Size} bytes total",
                uploadId, uploadedFiles.Count, totalSize);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                uploadId,
                files = uploadedFiles
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Upload");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred during file upload." });
            return errorResponse;
        }
    }

    private async Task ExtractZipToStagingAsync(string uploadId, string zipFileName, MemoryStream zipContent)
    {
        using var archive = new ZipArchive(zipContent, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms);
            ms.Position = 0;

            await _storageService.UploadStagingFileAsync(uploadId, entry.Name, ms);
        }
    }

    private static string? GetBoundary(string contentType)
    {
        var parts = contentType.Split(';', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                return part["boundary=".Length..].Trim('"');
            }
        }
        return null;
    }

    /// <summary>
    /// Simple multipart form data parser for file uploads.
    /// </summary>
    private static async Task<List<(string FileName, MemoryStream Content)>> ParseMultipartAsync(
        Stream body, string boundary)
    {
        var results = new List<(string FileName, MemoryStream Content)>();
        var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        var delimiterStart = $"--{boundary}";
        var delimiterEnd = $"--{boundary}--";
        var sections = content.Split(delimiterStart, StringSplitOptions.RemoveEmptyEntries);

        foreach (var section in sections)
        {
            if (section.Trim() == "--" || section.Trim().StartsWith("--")) continue;

            // Find the Content-Disposition header with filename
            var headerEnd = section.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) continue;

            var headers = section[..headerEnd];
            var fileBody = section[(headerEnd + 4)..];

            // Remove trailing \r\n
            if (fileBody.EndsWith("\r\n"))
                fileBody = fileBody[..^2];

            // Extract filename from Content-Disposition
            var fileNameMatch = System.Text.RegularExpressions.Regex.Match(
                headers, @"filename=""?([^"";\r\n]+)""?");
            if (!fileNameMatch.Success) continue;

            var fileName = fileNameMatch.Groups[1].Value.Trim();
            var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileBody));
            results.Add((fileName, ms));
        }

        return results;
    }
}
