using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Company.Function.Models;
using Company.Function.Utilities;
using Microsoft.Extensions.Logging;

namespace Company.Function.Services;

public class NotificationService
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        var connectionString = Environment.GetEnvironmentVariable("STORAGE")
            ?? throw new InvalidOperationException("STORAGE connection string not configured.");
        _tableServiceClient = new TableServiceClient(connectionString);
        _logger = logger;
    }

    public async Task NotifyRunCompletedAsync(PackagingRunEntity run)
    {
        var prefs = await GetGlobalPreferencesAsync();
        if (prefs == null) return;

        var isSuccess = run.Status == RunStatus.Succeeded || run.Status == RunStatus.SucceededWithWarnings;
        var isFailure = run.Status == RunStatus.Failed;

        // Teams webhook notification
        if (!string.IsNullOrEmpty(prefs.TeamsWebhookUrl))
        {
            if ((isSuccess && prefs.TeamsOnSuccess) || (isFailure && prefs.TeamsOnFailure))
            {
                await SendTeamsNotificationAsync(prefs.TeamsWebhookUrl, run);
            }
        }

        // Email notification via SendGrid
        var sendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        if (!string.IsNullOrEmpty(sendGridApiKey) && !string.IsNullOrEmpty(prefs.EmailAddress))
        {
            if ((isSuccess && prefs.EmailOnSuccess) || (isFailure && prefs.EmailOnFailure))
            {
                await SendEmailNotificationAsync(sendGridApiKey, prefs.EmailAddress, run);
            }
        }
    }

    private async Task SendTeamsNotificationAsync(string webhookUrl, PackagingRunEntity run)
    {
        try
        {
            var card = new
            {
                type = "message",
                attachments = new[] {
                    new {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[] {
                                new { type = "TextBlock", text = $"Packaging Run {run.Status}", weight = "Bolder", size = "Medium", color = run.Status == RunStatus.Failed ? "Attention" : "Good" },
                                new { type = "FactSet", facts = new[] {
                                    new { title = "App", value = run.AppName },
                                    new { title = "Version", value = run.Version },
                                    new { title = "Status", value = run.Status },
                                    new { title = "Run ID", value = run.RunId },
                                    new { title = "Started by", value = run.CreatedByName ?? "—" }
                                }}
                            }
                        }
                    }
                }
            };

            var response = await SharedHttpClient.PostAsJsonAsync(webhookUrl, card);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Teams webhook returned {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Teams notification for run {RunId}", run.RunId);
        }
    }

    private async Task SendEmailNotificationAsync(string apiKey, string toEmail, PackagingRunEntity run)
    {
        try
        {
            var fromEmail = Environment.GetEnvironmentVariable("SENDGRID_FROM_EMAIL") ?? "noreply@packaging.nouryon.com";
            var subject = $"[Packaging] {run.AppName} v{run.Version} - {run.Status}";
            var body = $"Packaging run {run.RunId} for {run.AppName} v{run.Version} completed with status: {run.Status}.";
            if (run.ErrorSummary != null) body += $"\n\nError: {run.ErrorSummary}";

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                personalizations = new[] { new { to = new[] { new { email = toEmail } } } },
                from = new { email = fromEmail },
                subject,
                content = new[] { new { type = "text/plain", value = body } }
            });

            var response = await SharedHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SendGrid returned {Status} for run {RunId}", response.StatusCode, run.RunId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email notification for run {RunId}", run.RunId);
        }
    }

    private async Task<TableClient> GetPreferencesTableAsync()
    {
        var client = _tableServiceClient.GetTableClient(TableNames.NotificationPreferences);
        await client.CreateIfNotExistsAsync();
        return client;
    }

    public async Task<NotificationPreferenceEntity?> GetGlobalPreferencesAsync()
    {
        var table = await GetPreferencesTableAsync();
        try
        {
            var entity = await table.GetEntityAsync<NotificationPreferenceEntity>("global", "default");
            return entity?.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SavePreferencesAsync(NotificationPreferenceEntity prefs)
    {
        var table = await GetPreferencesTableAsync();
        await table.UpsertEntityAsync(prefs, TableUpdateMode.Replace);
    }
}
