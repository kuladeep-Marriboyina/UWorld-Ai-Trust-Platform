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

// --- Detection engine + all rules (deterministic, versioned) ------------
builder.Services.AddSingleton<IDetectionRule, AuthorizationGapRule>();
builder.Services.AddSingleton<IDetectionRule, EntitlementLeakageRule>();
builder.Services.AddSingleton<IDetectionRule, RevenueDiscountAbuseRule>();
builder.Services.AddSingleton<IDetectionRule, ContentScrapingRule>();
builder.Services.AddSingleton<IDetectionRule, CoordinatedAccountRule>();
builder.Services.AddSingleton<IDetectionRule, CveExposureRule>();
// Phase-2 real-schema fraud rules
builder.Services.AddSingleton<IDetectionRule, PaidWithoutBraintreeRule>();
builder.Services.AddSingleton<IDetectionRule, DiscountCodeFraudRule>();
builder.Services.AddSingleton<IDetectionRule, SameSubscriptionDifferentRegionRule>();
builder.Services.AddSingleton<IDetectionRule, BlockedIpEmbargoRule>();
// Config-driven rules + NL-authored declarative rules
builder.Services.AddSingleton<RuleConfigStore>();
builder.Services.AddSingleton<IDeclarativeRuleSource, DeclarativeRuleSource>();
builder.Services.AddSingleton(SchemaCatalogFactory.Build());
builder.Services.AddScoped<IRuleRepository, RuleRepository>();
builder.Services.AddScoped<RuleConfigLoader>();
builder.Services.AddSingleton<IRuleInterpreter, MockRuleInterpreter>();
builder.Services.AddSingleton<DetectionRuleEngine>();

// --- AI copilot (pluggable; mock today, enterprise LLM later) -----------
builder.Services.AddSingleton<ISignalRedactor, SignalRedactor>();
builder.Services.AddSingleton<IPromptInjectionGuard, PromptInjectionGuard>();
builder.Services.AddSingleton(AiPricing.Default);
builder.Services.AddSingleton<CostCalculator>();
builder.Services.AddSingleton<ICopilot, MockCopilot>();

// --- Ingestion (modular monolith: the worker runs in-process) -----------
builder.Services.AddSingleton<INewRelicClient, MockNewRelicClient>();
builder.Services.AddSingleton(new IngestionOptions());
builder.Services.AddSingleton<IngestionWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestionWorker>());
builder.Services.AddScoped<DemoSimulator>();

// --- Dev info (Development only): request/response capture + code investigator ---
builder.Services.AddSingleton<CodeInvestigator>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Load rule config (enabled/thresholds/declarative) into the store before the worker runs.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<RuleConfigLoader>().ReloadAsync();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseMiddleware<RequestTracingMiddleware>();
if (app.Environment.IsDevelopment())
    app.UseMiddleware<DevCaptureMiddleware>();
app.UseMiddleware<RoleContextMiddleware>();
app.MapControllers();

app.Run();

public partial class Program { }   // exposed for integration testing
