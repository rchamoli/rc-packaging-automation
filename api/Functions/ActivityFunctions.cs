using System.Net;
using Company.Function.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class ActivityFunctions
{
    private readonly ActivityService _activityService;
    private readonly ILogger<ActivityFunctions> _logger;

    public ActivityFunctions(ActivityService activityService, ILogger<ActivityFunctions> logger)
    {
        _activityService = activityService;
        _logger = logger;
    }

    [Function("GetActivityLog")]
    public async Task<HttpResponseData> GetActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "activity")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/activity");
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var eventType = query["eventType"];

            var activities = await _activityService.GetRecentAsync(50, eventType);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                activities = activities.Select(a => new
                {
                    eventType = a.EventType,
                    userId = a.UserId,
                    userDisplayName = a.UserDisplayName,
                    description = a.Description,
                    runId = a.RunId,
                    appName = a.AppName,
                    occurredAt = a.OccurredAt
                })
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in GetActivity");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to load activity log." });
            return errorResponse;
        }
    }
}
