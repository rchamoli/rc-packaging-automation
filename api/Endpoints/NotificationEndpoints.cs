using Company.Function.Models;
using Company.Function.Services;
using Company.Function.Utilities;

namespace Company.Function.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notifications/preferences", GetPreferences);
        app.MapPut("/api/notifications/preferences", SavePreferences);
    }

    private static async Task<IResult> GetPreferences(
        NotificationService notificationService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("NotificationEndpoints");
        logger.LogInformation("GET /api/notifications/preferences");
        try
        {
            var prefs = await notificationService.GetGlobalPreferencesAsync();
            return Results.Ok(prefs ?? new NotificationPreferenceEntity());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in GetPreferences");
            return Results.Json(new { error = "Failed to load notification preferences." }, statusCode: 500);
        }
    }

    private static async Task<IResult> SavePreferences(
        HttpContext context,
        NotificationService notificationService,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("NotificationEndpoints");
        logger.LogInformation("PUT /api/notifications/preferences");
        try
        {
            var principal = AuthHelper.GetClientPrincipal(context.Request);
            if (!AuthHelper.IsAdmin(principal))
                return Results.Json(new { error = "Only admins can change notification preferences." }, statusCode: 403);

            var body = await context.Request.ReadFromJsonAsync<NotificationPreferenceEntity>();
            if (body == null)
                return Results.BadRequest(new { error = "Request body is required." });

            body.PartitionKey = "global";
            body.RowKey = "default";
            await notificationService.SavePreferencesAsync(body);

            return Results.Ok(new { saved = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in SavePreferences");
            return Results.Json(new { error = "Failed to save notification preferences." }, statusCode: 500);
        }
    }
}
