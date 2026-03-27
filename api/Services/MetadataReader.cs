using System.Text.Json;
using Company.Function.Models;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class MetadataReader
{
    private const string MetadataFileName = "release-metadata.json";
    private readonly ILogger<MetadataReader> _logger;

    public MetadataReader(ILogger<MetadataReader> logger)
    {
        _logger = logger;
    }

    private const long MaxMetadataFileSizeBytes = 1_048_576; // 1 MB

    public async Task<(ReleaseMetadata? Metadata, string? Error)> ReadAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return (null, "Release folder path is required.");

        // Resolve to full path to prevent path traversal
        var resolvedPath = Path.GetFullPath(folderPath);

        if (!Directory.Exists(resolvedPath))
            return (null, $"Release folder not found: {resolvedPath}");

        var metadataPath = Path.GetFullPath(Path.Combine(resolvedPath, MetadataFileName));

        // Ensure metadata path is within the resolved folder (prevent traversal via filename)
        if (!metadataPath.StartsWith(resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal detected: metadata path {MetadataPath} escapes folder {Folder}", metadataPath, resolvedPath);
            return (null, "Invalid folder path: potential path traversal detected.");
        }

        if (!File.Exists(metadataPath))
            return (null, $"Metadata file '{MetadataFileName}' not found in {resolvedPath}");

        try
        {
            // Check file size before reading into memory
            var fileInfo = new FileInfo(metadataPath);
            if (fileInfo.Length > MaxMetadataFileSizeBytes)
                return (null, $"Metadata file exceeds maximum size (1 MB). Actual: {fileInfo.Length / 1024} KB.");

            var json = await File.ReadAllTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<ReleaseMetadata>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (metadata is null)
                return (null, "Failed to parse release-metadata.json — file is empty or invalid.");

            var validationErrors = metadata.Validate();
            if (validationErrors.Count > 0)
                return (null, $"Invalid release-metadata.json: {string.Join(" ", validationErrors)}");

            _logger.LogInformation("Read metadata for {AppName} v{Version}", metadata.ApplicationName, metadata.ReleaseVersion);
            return (metadata, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON parse error in {Path}", metadataPath);
            return (null, $"The file {MetadataFileName} contains invalid JSON. Please check for syntax errors such as missing commas, brackets, or quotes.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error reading {Path}", metadataPath);
            return (null, $"Could not read {MetadataFileName}. The file may be locked or inaccessible.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reading metadata from {Path}", metadataPath);
            return (null, $"An unexpected error occurred while reading {MetadataFileName}. Please try again.");
        }
    }
}
