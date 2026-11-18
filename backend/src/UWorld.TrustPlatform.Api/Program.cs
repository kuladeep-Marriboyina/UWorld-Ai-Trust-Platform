using System.Text.Json.Serialization;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.DevTools;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Detection.Rules;
using UWorld.TrustPlatform.Ingestion;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("UWorldTrust")
    ?? @"Server=(localdb)\MSSQLLocalDB;Database=UWorldTrust;Trusted_Connection=True;TrustServerCertificate=True;";

// --- Data (raw ADO.NET) -------------------------------------------------
builder.Services.AddSingleton<ISqlConnectionFactory>(_ => new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<ISignalRepository, SignalRepository>();
builder.Services.AddScoped<IRiskCaseRepository, RiskCaseRepository>();
builder.Services.AddScoped<IUsageLedger, UsageLedgerRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IDevRequestLog, DevRequestLogRepository>();
builder.Services.AddScoped<IIdentityRepository, IdentityRepository>();
builder.Services.AddScoped<IRuleRepository, RuleRepository>();

// --- Detection engine: coded rules + governed, analyst-authored rules -----
builder.Services.AddSingleton<RuleConfigStore>();
builder.Services.AddSingleton<AccountFarmRule>();
builder.Services.AddSingleton<IDetectionRule>(sp => sp.GetRequiredService<AccountFarmRule>());
builder.Services.AddSingleton<IDetectionRule, CredentialStuffingRule>();
builder.Services.AddSingleton<IDetectionRule, PaidWithoutBraintreeRule>();
builder.Services.AddSingleton<IDetectionRule, DiscountCodeFraudRule>();
builder.Services.AddSingleton<IDetectionRule, SameSubscriptionDifferentRegionRule>();
builder.Services.AddSingleton<IDetectionRule, BlockedIpEmbargoRule>();
builder.Services.AddSingleton<IDetectionRule, AuthorizationGapRule>();
builder.Services.AddSingleton<IDetectionRule, EntitlementLeakageRule>();
builder.Services.AddSingleton<IDetectionRule, RevenueDiscountAbuseRule>();
builder.Services.AddSingleton<IDetectionRule, ContentScrapingRule>();
builder.Services.AddSingleton<IDetectionRule, CoordinatedAccountRule>();
builder.Services.AddSingleton<IDetectionRule, CveExposureRule>();
builder.Services.AddSingleton<IDeclarativeRuleSource, DeclarativeRuleSource>();
builder.Services.AddSingleton(SchemaCatalogFactory.Build());
builder.Services.AddScoped<RuleConfigLoader>();
builder.Services.AddSingleton<DetectionRuleEngine>();

// --- AI: Claude when credentials exist, deterministic twin otherwise ------
builder.Services.AddSingleton<ISignalRedactor, SignalRedactor>();
builder.Services.AddSingleton<IPromptInjectionGuard, PromptInjectionGuard>();
builder.Services.AddSingleton(AiPricing.Default);
builder.Services.AddSingleton<CostCalculator>();
builder.Services.AddSingleton<IAiProvider, ClaudeProvider>();
builder.Services.AddSingleton<ICaseCopilot, CaseCopilot>();
builder.Services.AddSingleton<IRuleDrafter, RuleDrafter>();

// --- Live: event-driven detection + Server-Sent Events -------------------
builder.Services.AddSingleton<LiveEventHub>();
builder.Services.AddScoped<IDetectionObserver, LiveDetectionObserver>();
builder.Services.AddSingleton<INewRelicClient, MockNewRelicClient>();
builder.Services.AddSingleton(new IngestionOptions());
builder.Services.AddSingleton<IngestionWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestionWorker>());
builder.Services.AddScoped<DemoSimulator>();
builder.Services.AddScoped<InvestigationService>();
builder.Services.AddScoped<DemoResetService>();

// --- Dev info (Development only) -----------------------------------------
builder.Services.AddSingleton<CodeInvestigator>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
     .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Request-Id")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RuleConfigLoader>().ReloadAsync();
    try { await LiveHistory.HydrateAsync(scope.ServiceProvider); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Live feed history unavailable"); }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseMiddleware<RequestTracingMiddleware>();
app.UseMiddleware<RoleContextMiddleware>();
if (app.Environment.IsDevelopment())
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api/live"), branch => branch.UseMiddleware<DevCaptureMiddleware>());
app.MapControllers();

app.Run();

public partial class Program { }   // exposed for integration testing
