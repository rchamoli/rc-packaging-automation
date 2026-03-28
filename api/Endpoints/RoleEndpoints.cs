using Company.Function.Utilities;

namespace Company.Function.Endpoints;

public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this WebApplication app)
    {
        app.MapGet("/api/roles", GetRoles);
    }

    private static Task<IResult> GetRoles(
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("RoleEndpoints");
        logger.LogInformation("GET /api/roles");

        var principal = AuthHelper.GetClientPrincipal(context.Request);
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

        roles.Add("viewer");

        return Task.FromResult(Results.Ok(new { roles }));
    }
}
