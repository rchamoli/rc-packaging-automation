using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function;

public class IntuneAppFunctions
{
    private readonly IntuneGraphService _intuneService;
    private readonly StorageService _storageService;
    private readonly MetadataReader _metadataReader;
    private readonly ActivityService _activityService;
    private readonly ILogger<IntuneAppFunctions> _logger;

    public IntuneAppFunctions(
        IntuneGraphService intuneService,
        StorageService storageService,
        MetadataReader metadataReader,
        ActivityService activityService,
        ILogger<IntuneAppFunctions> logger)
    {
        _intuneService = intuneService;
        _storageService = storageService;
        _metadataReader = metadataReader;
        _activityService = activityService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/intune/create-from-run/{runId}
    /// Creates an Intune Win32 app from a completed packaging run.
    /// Idempotent: returns existing app info if already created.
    /// </summary>
    [Function("CreateIntuneAppFromRun")]
    public async Task<HttpResponseData> CreateFromRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "intune/create-from-run/{runId}")] HttpRequestData req,
        string runId)
    {
        _logger.LogInformation("POST /api/intune/create-from-run/{RunId}", runId);

        try
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Run ID is required." });
                return badReq;
            }

            // Look up the packaging run
            var run = await _storageService.GetRunByIdAsync(runId);
            if (run is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Run not found." });
                return notFound;
            }

            // Verify run succeeded (accept both Succeeded and SucceededWithWarnings)
            if (run.Status != RunStatus.Succeeded && run.Status != RunStatus.SucceededWithWarnings)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = $"Run has status '{run.Status}'; Intune app creation requires a succeeded run." });
                return conflict;
            }

            // Idempotency: if Intune app was already created, return existing info
            if (!string.IsNullOrEmpty(run.IntuneAppId))
            {
                var existing = req.CreateResponse(HttpStatusCode.OK);
                await existing.WriteAsJsonAsync(new
                {
                    intuneAppId = run.IntuneAppId,
                    intuneAppLink = run.IntuneAppLink
                        ?? $"https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/{run.IntuneAppId}",
                    runId = run.RunId,
                    appName = run.AppName,
                    version = run.Version,
                    alreadyExisted = true
                });
                return existing;
            }

            // Read metadata: prefer stored snapshot, fall back to disk for pre-snapshot runs
            ReleaseMetadata? metadata;
            if (!string.IsNullOrEmpty(run.MetadataSnapshot))
            {
                metadata = JsonSerializer.Deserialize<ReleaseMetadata>(
                    run.MetadataSnapshot,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (metadata is null)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new { error = "Failed to deserialize stored metadata snapshot." });
                    return errorResponse;
                }
            }
            else
            {
                // Fallback: read from disk (backward compatibility with pre-snapshot runs)
                var (readMetadata, metadataError) = await _metadataReader.ReadAsync(run.SourceLocation);
                if (readMetadata is null)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteAsJsonAsync(new { error = $"Failed to read metadata: {metadataError}" });
                    return errorResponse;
                }
                metadata = readMetadata;
            }

            // Create the Intune app
            var (intuneAppId, intuneAppLink, createError) = await _intuneService.CreateFromRunAsync(run, metadata);

            if (intuneAppId is null)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = createError ?? "Failed to create Intune app. Please check Graph API configuration." });
                return errorResponse;
            }

            // Update the packaging run with the Intune app ID and link
            run.IntuneAppId = intuneAppId;
            run.IntuneAppLink = intuneAppLink;
            await _storageService.UpsertRunAsync(run);

            // Log activity
            var principal = AuthHelper.GetClientPrincipal(req);
            await _activityService.LogAsync(
                ActivityEventTypes.IntuneAppCreated,
                AuthHelper.GetUserId(principal),
                AuthHelper.GetUserDisplayName(principal),
                $"Created Intune app for {run.AppName} v{run.Version}",
                run.RunId,
                run.AppName);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                intuneAppId,
                intuneAppLink,
                runId = run.RunId,
                appName = run.AppName,
                version = run.Version
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in CreateFromRun for {RunId}", runId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred while creating the Intune app. Please try again." });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/intune/appref/resolve?appName=...&amp;version=...
    /// Resolves an Intune app reference by app name and version.
    /// </summary>
    [Function("ResolveIntuneAppRef")]
    public async Task<HttpResponseData> ResolveAppRef(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "intune/appref/resolve")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/intune/appref/resolve");

        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var appName = query["appName"];
            var version = query["version"];

            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(version))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Both 'appName' and 'version' query parameters are required." });
                return badReq;
            }

            var appRef = await _storageService.GetIntuneAppRefAsync(appName, version);
            if (appRef is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = $"No Intune app reference found for '{appName}' version '{version}'." });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                appName = appRef.AppName,
                version = appRef.Version,
                intuneAppId = appRef.IntuneAppId,
                intuneAppLink = appRef.IntuneAppLink,
                runId = appRef.RunId,
                createdAt = appRef.CreatedAt
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in ResolveAppRef");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred while resolving the app reference. Please try again." });
            return errorResponse;
        }
    }
}
