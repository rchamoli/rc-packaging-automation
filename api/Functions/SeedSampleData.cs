// ============================================================================
// TEMPLATE SAMPLE — Replace or delete this file when building your own feature.
//
// This is a working example of an idempotent seed-data endpoint. To create
// seed data for YOUR feature:
//   1. Copy this file and rename the class/function (e.g. SeedProjectsData).
//   2. Change the Route to "manage/seed-<your-feature>".
//   3. Change the table name and entity shape to match your domain.
//   4. Add a matching anonymous route in staticwebapp.config.swa.json.
//   5. Add the new endpoint URL to the seedDemoData() array in index.html.
//   6. Delete this sample file once you no longer need it.
//
// IMPORTANT: Customise users.json (solution root AND mock-oidc-provider/)
// with personas that fit your project's domain. Generic names like
// "Admin User" are not acceptable — use realistic names and descriptions
// for the specific application being built.
//
// See .github/skills/seed-data/SKILL.md for the full pattern.
// ============================================================================

using System.Net;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class SeedSampleData
{
    private readonly ILogger<SeedSampleData> _logger;

    public SeedSampleData(ILogger<SeedSampleData> logger)
    {
        _logger = logger;
    }

    [Function("SeedSampleData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/seed-sample")] HttpRequestData req)
    {
        _logger.LogInformation("Seeding sample data...");

        var connectionString = Environment.GetEnvironmentVariable("STORAGE");
        if (string.IsNullOrEmpty(connectionString))
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "STORAGE connection string not configured" });
            return errorResponse;
        }

        // TEMPLATE: Replace "SampleData" with your feature's table name.
        var tableClient = new TableServiceClient(connectionString).GetTableClient("SampleData");
        await tableClient.CreateIfNotExistsAsync();

        // User IDs MUST match users.json — update both files together
        // TEMPLATE: Replace these sample entities with your domain objects.
        var items = new List<TableEntity>
        {
            new("samples", "welcome-note")
            {
                { "Title", "Welcome to the Template" },
                { "Description", "This is a sample entity — replace with your own data." },
                { "CreatedBy", "user-admin" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "getting-started")
            {
                { "Title", "Getting Started" },
                { "Description", "Replace this seed data with your own feature data." },
                { "CreatedBy", "user-admin" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "manager-task")
            {
                { "Title", "Manager Task Example" },
                { "Description", "An example entity owned by the manager persona." },
                { "CreatedBy", "user-manager" },
                { "CreatedAt", DateTime.UtcNow }
            },
            new("samples", "team-member-task")
            {
                { "Title", "Team Member Task Example" },
                { "Description", "An example entity owned by the standard user persona." },
                { "CreatedBy", "user-standard" },
                { "CreatedAt", DateTime.UtcNow }
            }
        };

        var seeded = 0;
        foreach (var item in items)
        {
            await tableClient.UpsertEntityAsync(item, TableUpdateMode.Replace);
            seeded++;
        }

        _logger.LogInformation("Seeded {Count} sample entities", seeded);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded, table = "SampleData" });
        return response;
    }
}
