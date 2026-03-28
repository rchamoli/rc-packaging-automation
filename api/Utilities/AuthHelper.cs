using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Company.Function.Utilities;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Common claim type URIs used by App Service Easy Auth
    private const string ClaimNameIdentifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    private const string ClaimEmail = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress";
    private const string ClaimName = "name";
    private const string ClaimOid = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public static ClientPrincipal? GetClientPrincipal(HttpRequest req)
    {
        if (!req.Headers.TryGetValue("x-ms-client-principal", out var values))
            return null;

        var header = values.FirstOrDefault();
        if (string.IsNullOrEmpty(header)) return null;

        try
        {
            var decoded = Convert.FromBase64String(header);
            var json = Encoding.UTF8.GetString(decoded);
            var principal = JsonSerializer.Deserialize<ClientPrincipal>(json, JsonOptions);
            if (principal == null) return null;

            // Normalize: App Service Easy Auth uses auth_typ instead of identityProvider
            // and puts user info in claims rather than top-level fields.
            principal.IdentityProvider ??= principal.AuthTyp;

            if (string.IsNullOrEmpty(principal.UserId))
                principal.UserId = GetClaimValue(principal, ClaimNameIdentifier)
                    ?? GetClaimValue(principal, ClaimOid);

            if (string.IsNullOrEmpty(principal.UserDetails))
                principal.UserDetails = GetClaimValue(principal, ClaimEmail)
                    ?? GetClaimValue(principal, ClaimName);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public static string GetUserDisplayName(ClientPrincipal? principal)
    {
        if (principal == null) return "anonymous";
        var nameClaim = principal.Claims?.FirstOrDefault(c => c.Typ == ClaimName);
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

    private static string? GetClaimValue(ClientPrincipal principal, string claimType) =>
        principal.Claims?.FirstOrDefault(c =>
            string.Equals(c.Typ, claimType, StringComparison.OrdinalIgnoreCase))?.Val;
}

public class ClientPrincipal
{
    // SWA format fields
    public string? IdentityProvider { get; set; }
    public string? UserId { get; set; }
    public string? UserDetails { get; set; }
    public List<string>? UserRoles { get; set; }
    public List<ClientPrincipalClaim>? Claims { get; set; }

    // App Service Easy Auth format fields
    [JsonPropertyName("auth_typ")]
    public string? AuthTyp { get; set; }
    [JsonPropertyName("name_typ")]
    public string? NameTyp { get; set; }
    [JsonPropertyName("role_typ")]
    public string? RoleTyp { get; set; }
}

public class ClientPrincipalClaim
{
    public string Typ { get; set; } = string.Empty;
    public string Val { get; set; } = string.Empty;
}
