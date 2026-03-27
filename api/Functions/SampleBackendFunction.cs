// ============================================================================
// TEMPLATE SAMPLE — Replace or delete this file when building your own feature.
//
// This is a minimal Azure Function example showing the HTTP trigger pattern.
// Use it as a starting point, then delete it once you have real endpoints.
// ============================================================================

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class SampleBackendFunction
{
    private readonly ILogger<SampleBackendFunction> _logger;

    public SampleBackendFunction(ILogger<SampleBackendFunction> logger)
    {
        _logger = logger;
    }

    [Function("SampleBackendFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}
