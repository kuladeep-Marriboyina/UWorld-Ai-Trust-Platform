using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace UWorld.TrustPlatform.Api.Security;

/// <summary>
/// Lightweight role context for the PoC: the analyst workspace sends an X-Role header
/// (Analyst / Lead / ReadOnly). A real deployment swaps this for the existing .NET auth stack.
/// </summary>
public sealed class RoleContextMiddleware
{
    public const string ItemKey = "Role";
    private readonly RequestDelegate _next;

    public RoleContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var role = context.Request.Headers["X-Role"].FirstOrDefault();
        context.Items[ItemKey] = string.IsNullOrWhiteSpace(role) ? "Lead" : role.Trim();
        await _next(context);
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute : Attribute, IActionFilter
{
    private readonly string[] _allowed;
    public RequireRoleAttribute(params string[] allowed) => _allowed = allowed;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var role = context.HttpContext.Items[RoleContextMiddleware.ItemKey] as string ?? "ReadOnly";
        if (!_allowed.Contains(role, StringComparer.OrdinalIgnoreCase))
            context.Result = new ObjectResult(new { error = $"Role '{role}' may not perform this action. Requires: {string.Join(", ", _allowed)}." })
            { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
