using System.Net;
using Company.Function.Utilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class RoleFunctions
{
    private readonly ILogger<RoleFunctions> _logger;

    public RoleFunctions(ILogger<RoleFunctions> logger)
    {
        _logger = logger;
    }

    [Function("GetRoles")]
    public async Task<HttpResponseData> GetRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "roles")] HttpRequestData req)
    {
        _logger.LogInformation("GET /api/roles");

        var principal = AuthHelper.GetClientPrincipal(req);
        var roles = new List<string>();

        var adminGroupId = Environment.GetEnvironmentVariable("ROLE_ADMIN_GROUP_ID");
        var packagerGroupId = Environment.GetEnvironmentVariable("ROLE_PACKAGER_GROUP_ID");

        var groupClaims = principal?.Claims?
            .Where(c => c.Typ == "groups")
            .Select(c => c.Val)
            .ToList() ?? new List<string>();

        if (!string.IsNullOrEmpty(adminGroupId) && groupClaims.Contains(adminGroupId))
            roles.Add("admin");
        if (!string.IsNullOrEmpty(packagerGroupId) && groupClaims.Contains(packagerGroupId))
            roles.Add("packager");

        // Everyone who is authenticated is at least a viewer
        roles.Add("viewer");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { roles });
        return response;
    }
}
