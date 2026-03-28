using Company.Function.Utilities;

namespace Company.Function.Middleware;

/// <summary>
/// Requires authentication for all routes except a small anonymous allow-list.
/// Unauthenticated visitors are redirected to Azure AD login.
/// </summary>
public class AuthEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public AuthEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (!IsAnonymousRoute(path))
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

    private static bool IsAnonymousRoute(string path) =>
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/roles", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/manage/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/demo/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/.auth/", StringComparison.OrdinalIgnoreCase);
}
