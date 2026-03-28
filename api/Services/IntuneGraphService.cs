using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using CommitPostRequestBody = Microsoft.Graph.DeviceAppManagement.MobileApps.Item.GraphWin32LobApp.ContentVersions.Item.Files.Item.Commit.CommitPostRequestBody;

namespace Company.Function.Services;

public class IntuneGraphService
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly StorageService _storageService;
    private readonly ILogger<IntuneGraphService> _logger;

    // Cached Graph credentials for reuse
    private GraphServiceClient? _cachedGraphClient;
    private ClientSecretCredential? _cachedCredential;

    public IntuneGraphService(StorageService storageService, ILogger<IntuneGraphService> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Win32 app in Intune from a completed packaging run, including detection rules,
    /// dependencies, supersedence, and UAT assignment. Rolls back on failure.
    /// </summary>
    public async Task<(string? IntuneAppId, string? IntuneAppLink, string? Error)> CreateFromRunAsync(
        PackagingRunEntity run, ReleaseMetadata metadata)
    {
        GraphServiceClient graphClient;
        try
        {
            graphClient = GetOrCreateGraphClient();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Graph client configuration error");
            return (null, null, ex.Message);
        }

        string? createdAppId = null;

        try
        {
            // Build detection rules first — fail early if none can be generated
            var (detectionRules, rulesError) = BuildDetectionRulesChecked(metadata);
            if (detectionRules is null)
                return (null, null, rulesError);

            // Step 1: Create the Win32LobApp with detection rules
            _logger.LogInformation("Creating Win32LobApp in Intune for {AppName} v{Version}",
                metadata.ApplicationName, metadata.ReleaseVersion);

            var win32App = BuildWin32LobApp(metadata, detectionRules);
            var createdApp = await graphClient.DeviceAppManagement.MobileApps.PostAsync(win32App);

            if (createdApp?.Id is null)
                return (null, null, "Intune returned a null app ID after creation.");

            createdAppId = createdApp.Id;
            var intuneAppLink = $"https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/{createdAppId}";
            _logger.LogInformation("Created Intune app {IntuneAppId} for {AppName}", createdAppId, metadata.ApplicationName);

            // Step 2: Upload the .intunewin content if an artifact exists
            if (!string.IsNullOrEmpty(run.OutputArtifactPath))
            {
                try
                {
                    await UploadIntunewinContentAsync(graphClient, createdAppId, run);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upload .intunewin content for app {IntuneAppId}; app created but content not uploaded", createdAppId);
                }
            }
            else
            {
                _logger.LogWarning("No artifact path on run {RunId}; skipping content upload", run.RunId);
            }

            // Step 3: Configure dependencies (if not "none")
            if (!string.Equals(metadata.Dependencies, "none", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ConfigureRelationshipsAsync(createdAppId, metadata.Dependencies, "dependency");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to configure dependencies for app {IntuneAppId}", createdAppId);
                }
            }

            // Step 4: Configure supersedence (if not "none")
            if (!string.Equals(metadata.Supersedence, "none", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ConfigureRelationshipsAsync(createdAppId, metadata.Supersedence, "supersedence");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to configure supersedence for app {IntuneAppId}", createdAppId);
                }
            }

            // Step 5: Assign to UAT group (auto-creates if missing, auto-prefixes "UAT-")
            try
            {
                var uatGroupId = await AssignToUatGroupAsync(graphClient, createdAppId, metadata.UatGroup);
                run.UatGroupId = uatGroupId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to assign UAT group for app {IntuneAppId}", createdAppId);
            }

            // Step 6: Store reference mapping
            await _storageService.UpsertIntuneAppRefAsync(
                IntuneAppRefEntity.Create(
                    metadata.ApplicationName,
                    metadata.ReleaseVersion,
                    createdAppId,
                    intuneAppLink,
                    run.RunId));

            return (createdAppId, intuneAppLink, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Intune app for run {RunId}", run.RunId);

            // Rollback: attempt to delete the partially-created app
            if (createdAppId != null)
            {
                try
                {
                    await graphClient.DeviceAppManagement.MobileApps[createdAppId].DeleteAsync();
                    _logger.LogInformation("Rolled back partially-created Intune app {AppId}", createdAppId);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to clean up partially-created Intune app {AppId}", createdAppId);
                }
            }

            return (null, null, $"Intune app creation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a Win32LobApp from metadata with pre-built detection rules.
    /// </summary>
    internal static Win32LobApp BuildWin32LobApp(ReleaseMetadata metadata, List<Win32LobAppRule> detectionRules)
    {
        var app = new Win32LobApp
        {
            DisplayName = $"{metadata.ApplicationName} {metadata.ReleaseVersion}",
            Description = $"{metadata.ApplicationName} version {metadata.ReleaseVersion} (AN: {metadata.AnNumber})",
            Publisher = metadata.Publisher,
            InstallCommandLine = metadata.InstallCommand,
            UninstallCommandLine = metadata.UninstallCommand,
            SetupFilePath = DeriveSetupFileName(metadata),
            InstallExperience = new Win32LobAppInstallExperience
            {
                RunAsAccount = RunAsAccountType.System,
                DeviceRestartBehavior = Win32LobAppRestartBehavior.Suppress
            },
            ReturnCodes = new List<Win32LobAppReturnCode>
            {
                new() { ReturnCode = 0, Type = Win32LobAppReturnCodeType.Success },
                new() { ReturnCode = 1707, Type = Win32LobAppReturnCodeType.Success },
                new() { ReturnCode = 3010, Type = Win32LobAppReturnCodeType.SoftReboot },
                new() { ReturnCode = 1641, Type = Win32LobAppReturnCodeType.HardReboot },
                new() { ReturnCode = 1618, Type = Win32LobAppReturnCodeType.Retry }
            },
            Rules = detectionRules,
        };

        return app;
    }

    /// <summary>
    /// Derives the setup file name from the install command or installer type.
    /// </summary>
    internal static string DeriveSetupFileName(ReleaseMetadata metadata)
    {
        var command = metadata.InstallCommand?.Trim();
        if (!string.IsNullOrEmpty(command))
        {
            var firstToken = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstToken != null &&
                (firstToken.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 firstToken.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
                return firstToken;
        }
        return metadata.InstallerType.ToUpperInvariant() switch
        {
            "MSI" => "setup.msi",
            _ => "setup.exe"
        };
    }

    /// <summary>
    /// Combines the registry hive and path into a full registry key path.
    /// </summary>
    internal static string CombineRegistryHiveAndPath(string? hive, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;
        // If path already starts with a hive prefix, return as-is
        if (path.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            return path;
        if (string.IsNullOrWhiteSpace(hive))
            return path;
        var hivePrefix = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKU" or "HKEY_USERS" => "HKEY_USERS",
            _ => hive
        };
        return $"{hivePrefix}\\{path.TrimStart('\\')}";
    }

    /// <summary>
    /// Builds detection rules and validates at least one was generated.
    /// </summary>
    internal static (List<Win32LobAppRule>? Rules, string? Error) BuildDetectionRulesChecked(ReleaseMetadata metadata)
    {
        var rules = BuildDetectionRules(metadata);
        if (rules.Count == 0)
            return (null, "No detection rules could be generated from the provided metadata. Ensure registry path or MSI product code is provided.");
        return (rules, null);
    }

    /// <summary>
    /// Builds detection rules from metadata: registry key/value primary; MSI product code only for true MSI.
    /// Uses registryHive to build the full registry key path.
    /// </summary>
    internal static List<Win32LobAppRule> BuildDetectionRules(ReleaseMetadata metadata)
    {
        var rules = new List<Win32LobAppRule>();

        // Primary: Registry detection rule
        if (metadata.DetectionType.Equals("registry", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(metadata.RegistryPath))
        {
            var registryRule = new Win32LobAppRegistryRule
            {
                RuleType = Win32LobAppRuleType.Detection,
                KeyPath = CombineRegistryHiveAndPath(metadata.RegistryHive, metadata.RegistryPath),
                ValueName = metadata.RegistryValueName ?? string.Empty,
                Check32BitOn64System = metadata.RegistryArchitecture?.Equals("x86", StringComparison.OrdinalIgnoreCase) == true,
                OperationType = ParseRegistryOperationType(metadata.RegistryRuleType),
                ComparisonValue = metadata.RegistryExpectedValue,
                Operator = Win32LobAppRuleOperator.Equal,
            };

            rules.Add(registryRule);
        }

        // Secondary: MSI product code detection (only for true MSI installers)
        if (metadata.InstallerType.Equals("MSI", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(metadata.MsiProductCode))
        {
            var msiRule = new Win32LobAppProductCodeRule
            {
                RuleType = Win32LobAppRuleType.Detection,
                ProductCode = metadata.MsiProductCode,
                ProductVersionOperator = Win32LobAppRuleOperator.GreaterThanOrEqual,
                ProductVersion = metadata.ReleaseVersion,
            };

            rules.Add(msiRule);
        }

        // Fallback: if no rules were generated, add a registry rule with a version-based path
        if (rules.Count == 0 && !string.IsNullOrWhiteSpace(metadata.RegistryPath))
        {
            rules.Add(new Win32LobAppRegistryRule
            {
                RuleType = Win32LobAppRuleType.Detection,
                KeyPath = CombineRegistryHiveAndPath(metadata.RegistryHive, metadata.RegistryPath),
                ValueName = metadata.RegistryValueName ?? "DisplayVersion",
                Check32BitOn64System = false,
                OperationType = Win32LobAppRegistryRuleOperationType.Exists,
            });
        }

        return rules;
    }

    private static Win32LobAppRegistryRuleOperationType? ParseRegistryOperationType(string? ruleType)
    {
        if (string.IsNullOrWhiteSpace(ruleType)) return Win32LobAppRegistryRuleOperationType.Exists;

        return ruleType.ToLowerInvariant() switch
        {
            "exists" => Win32LobAppRegistryRuleOperationType.Exists,
            "doesnotexist" or "does_not_exist" => Win32LobAppRegistryRuleOperationType.DoesNotExist,
            "string" or "stringcomparison" => Win32LobAppRegistryRuleOperationType.String,
            "integer" or "integercomparison" => Win32LobAppRegistryRuleOperationType.Integer,
            "version" or "versioncomparison" => Win32LobAppRegistryRuleOperationType.Version,
            _ => Win32LobAppRegistryRuleOperationType.Exists
        };
    }

    /// <summary>
    /// Uploads the .intunewin content to the Intune app via Graph content version flow.
    /// </summary>
    private async Task UploadIntunewinContentAsync(GraphServiceClient graphClient, string appId, PackagingRunEntity run)
    {
        _logger.LogInformation("Starting content upload for app {AppId}, artifact {ArtifactPath}", appId, run.OutputArtifactPath);

        // Download .intunewin from blob storage to a temp file
        var tempDir = Path.Combine(Path.GetTempPath(), $"intune-upload-{run.RunId}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var connectionString = Environment.GetEnvironmentVariable("STORAGE")
                ?? throw new InvalidOperationException("STORAGE connection string not configured.");
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(BlobContainers.Artifacts);
            var blobClient = containerClient.GetBlobClient(run.OutputArtifactPath);

            var tempFilePath = Path.Combine(tempDir, Path.GetFileName(run.OutputArtifactPath!));
            await using (var fileStream = File.Create(tempFilePath))
            {
                await blobClient.DownloadToAsync(fileStream);
            }

            var fileInfo = new FileInfo(tempFilePath);
            _logger.LogInformation("Downloaded artifact to {TempFile} ({Size} bytes)", tempFilePath, fileInfo.Length);

            // Determine the encrypted file size from the .intunewin package
            var encryptedSize = fileInfo.Length;
            var unencryptedSize = GetUnencryptedSize(tempFilePath);

            // Create content version
            var contentVersion = await graphClient.DeviceAppManagement.MobileApps[appId]
                .GraphWin32LobApp.ContentVersions
                .PostAsync(new MobileAppContent());

            if (contentVersion?.Id is null)
            {
                _logger.LogWarning("Failed to create content version for app {AppId}", appId);
                return;
            }

            // Create content file
            var contentFile = new MobileAppContentFile
            {
                Name = Path.GetFileName(tempFilePath),
                Size = unencryptedSize,
                SizeEncrypted = encryptedSize,
                IsDependency = false,
            };

            var createdFile = await graphClient.DeviceAppManagement.MobileApps[appId]
                .GraphWin32LobApp.ContentVersions[contentVersion.Id].Files
                .PostAsync(contentFile);

            if (createdFile?.Id is null)
            {
                _logger.LogWarning("Failed to create content file for app {AppId}", appId);
                return;
            }

            _logger.LogInformation("Created content file {FileId} for app {AppId}", createdFile.Id, appId);

            // Wait for Azure Storage URI to become available (poll with backoff)
            string? azureStorageUri = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));

                var fileStatus = await graphClient.DeviceAppManagement.MobileApps[appId]
                    .GraphWin32LobApp.ContentVersions[contentVersion.Id].Files[createdFile.Id]
                    .GetAsync();

                if (!string.IsNullOrEmpty(fileStatus?.AzureStorageUri))
                {
                    azureStorageUri = fileStatus.AzureStorageUri;
                    break;
                }
            }

            if (string.IsNullOrEmpty(azureStorageUri))
            {
                _logger.LogWarning("Azure Storage URI not available after polling for app {AppId}", appId);
                return;
            }

            // Upload file content to Azure Storage URI
            await UploadFileToAzureStorageAsync(tempFilePath, azureStorageUri);

            // Commit the file
            await graphClient.DeviceAppManagement.MobileApps[appId]
                .GraphWin32LobApp.ContentVersions[contentVersion.Id].Files[createdFile.Id]
                .Commit.PostAsync(new CommitPostRequestBody
                {
                    FileEncryptionInfo = ExtractEncryptionInfo(tempFilePath)
                });

            _logger.LogInformation("Content upload completed for app {AppId}", appId);

            // Update the app to reference the committed content version
            await graphClient.DeviceAppManagement.MobileApps[appId].PatchAsync(new Win32LobApp
            {
                CommittedContentVersion = contentVersion.Id
            });
        }
        finally
        {
            // Clean up temp files
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to clean up temp dir {TempDir}", tempDir); }
        }
    }

    /// <summary>
    /// Uploads a file to the Azure Storage URI provided by Intune in 6 MiB blocks with retry.
    /// </summary>
    private async Task UploadFileToAzureStorageAsync(string filePath, string azureStorageUri)
    {
        const int blockSize = 6 * 1024 * 1024; // 6 MiB blocks
        const int maxRetries = 3;
        var blockIds = new List<string>();
        var buffer = new byte[blockSize];

        var blobClient = new Azure.Storage.Blobs.Specialized.BlockBlobClient(new Uri(azureStorageUri));

        await using var fileStream = File.OpenRead(filePath);
        int blockNumber = 0;
        int bytesRead;

        while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory())) > 0)
        {
            var blockId = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(blockNumber.ToString("D6")));
            blockIds.Add(blockId);

            // Retry per block with exponential backoff
            for (int retry = 0; retry <= maxRetries; retry++)
            {
                try
                {
                    using var blockStream = new MemoryStream(buffer, 0, bytesRead);
                    await blobClient.StageBlockAsync(blockId, blockStream);
                    break;
                }
                catch (Exception ex) when (retry < maxRetries)
                {
                    _logger.LogWarning(ex, "Block {Block} upload failed (attempt {Attempt}/{Max}), retrying",
                        blockNumber, retry + 1, maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retry)));
                }
            }
            blockNumber++;
        }

        await blobClient.CommitBlockListAsync(blockIds);
        _logger.LogInformation("Uploaded {Blocks} blocks to Azure Storage", blockNumber);
    }

    /// <summary>
    /// Configures dependency or supersedence relationships using the Graph API.
    /// Resolves targets by App Name + Version via the IntuneAppRef table.
    /// Format: "AppName1|Version1,AppName2|Version2" or "none"
    /// </summary>
    private async Task ConfigureRelationshipsAsync(string appId, string input, string relationshipType)
    {
        var resolved = await ParseRelationshipsAsync(input);
        if (resolved.Count == 0) return;

        var odataType = relationshipType == "supersedence"
            ? "#microsoft.graph.mobileAppSupersedence"
            : "#microsoft.graph.mobileAppDependency";

        var relationships = resolved.Select(r =>
        {
            var rel = new Dictionary<string, object>
            {
                ["@odata.type"] = odataType,
                ["targetId"] = r.IntuneAppId,
                ["targetDisplayName"] = $"{r.AppName} {r.Version}",
            };

            if (relationshipType == "supersedence")
                rel["supersedenceType"] = "update";
            else
                rel["dependencyType"] = "autoInstall";

            return rel;
        }).ToList();

        var body = JsonSerializer.Serialize(new { relationships },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var token = await GetAccessTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/deviceAppManagement/mobileApps/{Uri.EscapeDataString(appId)}/updateRelationships");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Accept", "application/json");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await SharedHttpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to update {Type} for app {AppId}: {Status} {Error}",
                relationshipType, appId, response.StatusCode, errorBody);
            return;
        }

        _logger.LogInformation("Configured {Count} {Type} entries for app {AppId}",
            resolved.Count, relationshipType, appId);
    }

    /// <summary>
    /// Parses relationship strings (format: "AppName|Version,AppName|Version") and resolves
    /// each target by looking up the IntuneAppRef table.
    /// </summary>
    private async Task<List<(string AppName, string Version, string IntuneAppId)>> ParseRelationshipsAsync(string input)
    {
        var results = new List<(string AppName, string Version, string IntuneAppId)>();

        if (string.IsNullOrWhiteSpace(input) || input.Equals("none", StringComparison.OrdinalIgnoreCase))
            return results;

        var entries = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                _logger.LogWarning("Skipping invalid relationship entry: {Entry}", entry);
                continue;
            }

            var appName = parts[0];
            var version = parts[1];

            var appRef = await _storageService.GetIntuneAppRefAsync(appName, version);
            if (appRef is null)
            {
                _logger.LogWarning("Could not resolve Intune app ref for {AppName} v{Version}; skipping", appName, version);
                continue;
            }

            results.Add((appName, version, appRef.IntuneAppId));
        }

        return results;
    }

    /// <summary>
    /// Assigns the Intune app to the UAT group.
    /// </summary>
    private async Task<string?> AssignToUatGroupAsync(GraphServiceClient graphClient, string appId, string uatGroup)
    {
        _logger.LogInformation("Assigning app {AppId} to UAT group {UatGroup}", appId, uatGroup);

        string? groupId;
        if (Guid.TryParse(uatGroup, out _))
        {
            groupId = uatGroup;
        }
        else
        {
            // Auto-prefix "UAT-" if not already present
            var normalizedName = uatGroup;
            if (!normalizedName.StartsWith("UAT-", StringComparison.OrdinalIgnoreCase))
                normalizedName = "UAT-" + normalizedName;

            groupId = await ResolveOrCreateGroupAsync(graphClient, normalizedName);
            if (groupId is null)
            {
                _logger.LogWarning("Could not resolve or create UAT group '{UatGroup}'; skipping assignment", normalizedName);
                return null;
            }
        }

        var assignment = new MobileAppAssignment
        {
            Target = new GroupAssignmentTarget
            {
                GroupId = groupId
            },
            Intent = InstallIntent.Available,
        };

        await graphClient.DeviceAppManagement.MobileApps[appId].Assignments.PostAsync(assignment);
        _logger.LogInformation("Assigned app {AppId} to group {GroupId}", appId, groupId);
        return groupId;
    }

    private async Task<string?> ResolveOrCreateGroupAsync(GraphServiceClient graphClient, string groupDisplayName)
    {
        var escapedName = groupDisplayName.Replace("'", "''");

        var groups = await graphClient.Groups.GetAsync(config =>
        {
            config.QueryParameters.Filter = $"displayName eq '{escapedName}'";
            config.QueryParameters.Top = 1;
            config.QueryParameters.Select = new[] { "id", "displayName" };
        });

        var existing = groups?.Value?.FirstOrDefault();
        if (existing?.Id is not null)
            return existing.Id;

        // Group not found — create it as an empty security group
        _logger.LogInformation("UAT group '{GroupName}' not found in Azure AD; creating it", groupDisplayName);

        var newGroup = new Microsoft.Graph.Models.Group
        {
            DisplayName = groupDisplayName,
            Description = $"UAT testing group for {groupDisplayName} — auto-created by Packaging Automation",
            MailEnabled = false,
            MailNickname = groupDisplayName.Replace(" ", "-").ToLowerInvariant(),
            SecurityEnabled = true,
            GroupTypes = new List<string>()
        };

        var created = await graphClient.Groups.PostAsync(newGroup);
        if (created?.Id is null)
        {
            _logger.LogWarning("Failed to create UAT group '{GroupName}'", groupDisplayName);
            return null;
        }

        _logger.LogInformation("Created UAT group '{GroupName}' with ID {GroupId}", groupDisplayName, created.Id);
        return created.Id;
    }

    /// <summary>
    /// Gets or creates a cached GraphServiceClient using client credentials from environment variables.
    /// </summary>
    private GraphServiceClient GetOrCreateGraphClient()
    {
        if (_cachedGraphClient != null) return _cachedGraphClient;
        _cachedCredential = CreateCredential();
        _cachedGraphClient = new GraphServiceClient(_cachedCredential, new[] { "https://graph.microsoft.com/.default" });
        return _cachedGraphClient;
    }

    // Keep the static factory for backward compatibility (used in tests)
    internal static GraphServiceClient CreateGraphClient()
    {
        var credential = CreateCredential();
        return new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
    }

    private async Task<string> GetAccessTokenAsync()
    {
        _cachedCredential ??= CreateCredential();
        var tokenResult = await _cachedCredential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
        return tokenResult.Token;
    }

    private static ClientSecretCredential CreateCredential()
    {
        var tenantId = Environment.GetEnvironmentVariable("GRAPH_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("GRAPH_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("GRAPH_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Graph API credentials not configured. Set GRAPH_TENANT_ID, GRAPH_CLIENT_ID, and GRAPH_CLIENT_SECRET environment variables.");
        }

        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }

    /// <summary>
    /// Gets the unencrypted file size from the .intunewin archive.
    /// Falls back to the encrypted file size if extraction fails.
    /// </summary>
    private long GetUnencryptedSize(string intunewinPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(intunewinPath);
            var detectionEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("Detection.xml", StringComparison.OrdinalIgnoreCase));

            if (detectionEntry is not null)
            {
                using var reader = new StreamReader(detectionEntry.Open());
                var xml = reader.ReadToEnd();

                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var sizeElement = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "UnencryptedContentSize");

                if (sizeElement is not null && long.TryParse(sizeElement.Value, out var size))
                    return size;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract unencrypted size from {Path}, falling back to file size", intunewinPath);
        }

        return new FileInfo(intunewinPath).Length;
    }

    /// <summary>
    /// Extracts encryption info from the .intunewin archive's Detection.xml.
    /// Returns null if extraction fails.
    /// </summary>
    private FileEncryptionInfo? ExtractEncryptionInfo(string intunewinPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(intunewinPath);
            var detectionEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("Detection.xml", StringComparison.OrdinalIgnoreCase));

            if (detectionEntry is null) return null;

            using var reader = new StreamReader(detectionEntry.Open());
            var xml = reader.ReadToEnd();
            var doc = System.Xml.Linq.XDocument.Parse(xml);

            string? GetValue(string elementName) =>
                doc.Descendants().FirstOrDefault(e => e.Name.LocalName == elementName)?.Value;

            var encryptionKey = GetValue("EncryptionKey");
            var initializationVector = GetValue("InitializationVector");
            var mac = GetValue("Mac");
            var macKey = GetValue("MacKey");
            var profileIdentifier = GetValue("ProfileIdentifier");
            var fileDigest = GetValue("FileDigest");
            var fileDigestAlgorithm = GetValue("FileDigestAlgorithm");

            if (encryptionKey is null || initializationVector is null || mac is null)
                return null;

            return new FileEncryptionInfo
            {
                EncryptionKey = Convert.FromBase64String(encryptionKey),
                InitializationVector = Convert.FromBase64String(initializationVector),
                Mac = Convert.FromBase64String(mac),
                MacKey = macKey is not null ? Convert.FromBase64String(macKey) : null,
                ProfileIdentifier = profileIdentifier,
                FileDigest = fileDigest is not null ? Convert.FromBase64String(fileDigest) : null,
                FileDigestAlgorithm = fileDigestAlgorithm,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract encryption info from {Path}", intunewinPath);
            return null;
        }
    }
}
