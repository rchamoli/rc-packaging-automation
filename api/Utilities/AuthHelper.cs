using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace Company.Function.Utilities;

public static class AuthHelper
{
    public static ClientPrincipal? GetClientPrincipal(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("x-ms-client-principal", out var values))
            return null;

        var header = values.FirstOrDefault();
        if (string.IsNullOrEmpty(header)) return null;

        try
        {
            var decoded = Convert.FromBase64String(header);
            var json = Encoding.UTF8.GetString(decoded);
            return JsonSerializer.Deserialize<ClientPrincipal>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public static string GetUserDisplayName(ClientPrincipal? principal)
    {
        if (principal == null) return "anonymous";
        var nameClaim = principal.Claims?.FirstOrDefault(c => c.Typ == "name");
        return nameClaim?.Val ?? principal.UserDetails ?? principal.UserId ?? "unknown";
    }

    public static string GetUserId(ClientPrincipal? principal)
    {
        return principal?.UserId ?? "anonymous";
    }

    public static bool HasRole(ClientPrincipal? principal, string role)
    {
        return principal?.UserRoles?.Contains(role, StringComparer.OrdinalIgnoreCase) == true;
    }

    public static bool IsAdmin(ClientPrincipal? principal) => HasRole(principal, "admin");
    public static bool IsPackager(ClientPrincipal? principal) => HasRole(principal, "packager") || IsAdmin(principal);
    public static bool IsViewer(ClientPrincipal? principal) => HasRole(principal, "viewer") || IsPackager(principal);
}

public class ClientPrincipal
{
    public string? IdentityProvider { get; set; }
    public string? UserId { get; set; }
    public string? UserDetails { get; set; }
    public List<string>? UserRoles { get; set; }
    public List<ClientPrincipalClaim>? Claims { get; set; }
}

public class ClientPrincipalClaim
{
    public string Typ { get; set; } = string.Empty;
    public string Val { get; set; } = string.Empty;
}
