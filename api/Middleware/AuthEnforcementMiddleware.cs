using Company.Function.Utilities;

namespace Company.Function.Middleware;

/// <summary>
/// Replaces SWA route-level auth. Redirects unauthenticated users to Azure AD login
/// for protected routes (/app/* and most /api/* routes).
/// </summary>
public class AuthEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public AuthEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        bool requiresAuth =
            path.StartsWith("/app/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/app", StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !IsAnonymousApiRoute(path));

        if (requiresAuth)
        {
            var principal = AuthHelper.GetClientPrincipal(context.Request);
            if (principal == null || string.IsNullOrEmpty(principal.UserId))
            {
                context.Response.Redirect("/.auth/login/aad?post_login_redirect_uri=/app/");
                return;
            }
        }

        await _next(context);
    }

    private static bool IsAnonymousApiRoute(string path) =>
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/roles", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/manage/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/demo/", StringComparison.OrdinalIgnoreCase);
}
