using System.Net;
using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class NotificationFunctions
{
    private readonly NotificationService _notificationService;
    private readonly ILogger<NotificationFunctions> _logger;

    public NotificationFunctions(NotificationService notificationService, ILogger<NotificationFunctions> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function("GetNotificationPreferences")]
    public async Task<HttpResponseData> GetPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/preferences")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/notifications/preferences");
        try
        {
            var prefs = await _notificationService.GetGlobalPreferencesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(prefs ?? new NotificationPreferenceEntity());
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in GetPreferences");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to load notification preferences." });
            return errorResponse;
        }
    }

    [Function("SaveNotificationPreferences")]
    public async Task<HttpResponseData> SavePreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "notifications/preferences")] HttpRequestData req)
    {
        _logger.LogInformation("PUT /api/notifications/preferences");
        try
        {
            var principal = AuthHelper.GetClientPrincipal(req);
            if (!AuthHelper.IsAdmin(principal))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only admins can change notification preferences." });
                return forbidden;
            }

            var body = await req.ReadFromJsonAsync<NotificationPreferenceEntity>();
            if (body == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Request body is required." });
                return badReq;
            }

            body.PartitionKey = "global";
            body.RowKey = "default";
            await _notificationService.SavePreferencesAsync(body);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { saved = true });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in SavePreferences");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to save notification preferences." });
            return errorResponse;
        }
    }
}
