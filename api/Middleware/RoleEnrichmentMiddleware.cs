using System.Text;
using System.Text.Json;
using Company.Function.Utilities;

namespace Company.Function.Middleware;

/// <summary>
/// Replaces SWA's rolesSource feature. Resolves Azure AD group claims
/// into app roles (admin, packager, viewer) and re-encodes the principal header.
/// </summary>
public class RoleEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public RoleEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey("x-ms-client-principal")
            && !context.Request.Path.Equals("/api/roles", StringComparison.OrdinalIgnoreCase))
        {
            var principal = AuthHelper.GetClientPrincipal(context.Request);
            if (principal != null)
            {
                var adminGroupId = Environment.GetEnvironmentVariable("ROLE_ADMIN_GROUP_ID");
                var packagerGroupId = Environment.GetEnvironmentVariable("ROLE_PACKAGER_GROUP_ID");

                var groupClaims = principal.Claims?
                    .Where(c => c.Typ == "groups")
                    .Select(c => c.Val)
                    .ToList() ?? new List<string>();

                var roles = new List<string>();
                if (!string.IsNullOrEmpty(adminGroupId) && groupClaims.Contains(adminGroupId))
                    roles.Add("admin");
                if (!string.IsNullOrEmpty(packagerGroupId) && groupClaims.Contains(packagerGroupId))
                    roles.Add("packager");
                roles.Add("viewer");

                principal.UserRoles = roles;

                var json = JsonSerializer.Serialize(principal,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                context.Request.Headers["x-ms-client-principal"] = encoded;
            }
        }

        await _next(context);
    }
}
