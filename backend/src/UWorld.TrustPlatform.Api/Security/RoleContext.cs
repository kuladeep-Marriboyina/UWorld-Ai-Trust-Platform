using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace UWorld.TrustPlatform.Api.Security;

public static class Roles
{
    public const string Viewer = "Viewer";          // read dashboards and cases
    public const string Analyst = "Analyst";        // + work cases, run simulations
    public const string RuleAuthor = "RuleAuthor";  // + propose rule changes
    public const string Admin = "Admin";            // + approve rules, enable/disable, reset demo

    public static readonly string[] CaseWorkers = { Analyst, RuleAuthor, Admin };
    public static readonly string[] Authors = { RuleAuthor, Admin };
}

public sealed record Persona(string Username, string DisplayName, string Role, string Title, string Initials);

/// <summary>Demo directory. A real deployment resolves the user and role from corporate SSO claims instead.</summary>
public static class PlatformDirectory
{
    public static readonly IReadOnlyList<Persona> People = new[]
    {
        new Persona("maya.chen", "Maya Chen", Roles.Admin, "Trust & Safety Lead", "MC"),
        new Persona("jordan.patel", "Jordan Patel", Roles.RuleAuthor, "Senior Fraud Analyst", "JP"),
        new Persona("sam.ortiz", "Sam Ortiz", Roles.Analyst, "Fraud Analyst", "SO"),
        new Persona("riley.kim", "Riley Kim", Roles.Viewer, "Finance Partner", "RK"),
    };

    public static Persona Resolve(string? username) =>
        People.FirstOrDefault(p => string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase)) ?? People[2];

    public static IReadOnlyList<string> Permissions(string role) => role switch
    {
        Roles.Admin => new[] { "view", "work-cases", "simulate", "propose-rules", "approve-rules", "toggle-rules", "reset-demo" },
        Roles.RuleAuthor => new[] { "view", "work-cases", "simulate", "propose-rules" },
        Roles.Analyst => new[] { "view", "work-cases", "simulate" },
        _ => new[] { "view" },
    };
}

/// <summary>Resolves the acting user from the X-User header (legacy X-Role still honoured).</summary>
public sealed class RoleContextMiddleware
{
    public const string ItemKey = "Role";
    public const string UserKey = "User";
    private readonly RequestDelegate _next;

    public RoleContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.Request.Headers["X-User"].FirstOrDefault();
        var legacyRole = context.Request.Headers["X-Role"].FirstOrDefault();
        var persona = string.IsNullOrWhiteSpace(user) && legacyRole is "Lead"
            ? PlatformDirectory.People[0]
            : PlatformDirectory.Resolve(user);
        context.Items[UserKey] = persona;
        context.Items[ItemKey] = persona.Role;
        await _next(context);
    }
}

public static class HttpContextUserExtensions
{
    public static Persona Persona(this HttpContext ctx) => ctx.Items[RoleContextMiddleware.UserKey] as Persona ?? PlatformDirectory.People[2];
    public static string Actor(this HttpContext ctx) => ctx.Persona().Username;
    public static string RequestId(this HttpContext ctx) => ctx.Items[RequestTracingMiddleware.ItemKey] as string ?? "req-adhoc";
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute : Attribute, IActionFilter
{
    private readonly string[] _allowed;
    public RequireRoleAttribute(params string[] allowed) => _allowed = allowed;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var persona = context.HttpContext.Persona();
        if (!_allowed.Contains(persona.Role, StringComparer.OrdinalIgnoreCase))
            context.Result = new ObjectResult(new
            {
                error = $"{persona.DisplayName} ({persona.Role}) can't do this. Requires: {string.Join(" or ", _allowed)}.",
                requiredRoles = _allowed
            })
            { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
