using System.Text.Json;
using Company.Function.BackgroundServices;
using Company.Function.Endpoints;
using Company.Function.Middleware;
using Company.Function.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// ── Services (unchanged from Azure Functions version) ────────────
builder.Services.AddSingleton<MetadataReader>();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<AppDataSeeder>();
builder.Services.AddSingleton<IntuneGraphService>();
builder.Services.AddSingleton<PackagingService>();
builder.Services.AddSingleton<ActivityService>();
builder.Services.AddSingleton<NotificationService>();

// ── Background processing (replaces Task.Run fire-and-forget) ────
builder.Services.AddSingleton<PackagingJobQueue>();
builder.Services.AddHostedService<PackagingBackgroundService>();

// ── JSON serialization (camelCase, case-insensitive) ─────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// ── Application Insights ─────────────────────────────────────────
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// ── Upload limits (500 MB) ────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────
// Auth & security middleware must run BEFORE static files so that
// /app/* pages are protected even when served from wwwroot.
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RoleEnrichmentMiddleware>();
app.UseMiddleware<AuthEnforcementMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/app"))
            ctx.Context.Response.Headers.CacheControl = "no-store";
    }
});

// ── Endpoint routing ─────────────────────────────────────────────
app.MapGet("/", () => Results.Redirect("/app/dashboard.html"));
app.MapGet("/app/", () => Results.Redirect("/app/dashboard.html"));

app.MapHealthEndpoints();
app.MapPackagingEndpoints();
app.MapIntuneAppEndpoints();
app.MapActivityEndpoints();
app.MapNotificationEndpoints();
app.MapUploadEndpoints();
app.MapRoleEndpoints();
app.MapDemoEndpoints();
app.MapSeedEndpoints();

app.Run();
