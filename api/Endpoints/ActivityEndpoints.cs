using Company.Function.Services;

namespace Company.Function.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/activity", GetActivity);
    }

    private static async Task<IResult> GetActivity(
        HttpContext context,
        ActivityService activityService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ActivityEndpoints");
        logger.LogInformation("GET /api/activity");
        try
        {
            var eventType = context.Request.Query["eventType"].FirstOrDefault();
            var activities = await activityService.GetRecentAsync(50, eventType);

            return Results.Ok(new
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in GetActivity");
            return Results.Json(new { error = "Failed to load activity log." }, statusCode: 500);
        }
    }
}
