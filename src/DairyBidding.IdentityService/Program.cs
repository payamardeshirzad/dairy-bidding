using System.Security.Claims;
using DairyBidding.IdentityService;
using DairyBidding.IdentityService.Data;
using DairyBidding.IdentityService.Extensions;
using DairyBidding.IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// --- Database ---
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required."))
        .UseSnakeCaseNamingConvention());

// --- Identity services ---
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// --- Duende IdentityServer ---
var issuerUri = builder.Configuration["Jwt:IssuerUri"] ?? "http://localhost:5245";

builder.Services.AddIdentityServer(options =>
    {
        options.IssuerUri = issuerUri;
    })
    .AddDeveloperSigningCredential()
    .AddInMemoryIdentityResources(IdentityServerConfig.IdentityResources)
    .AddInMemoryApiScopes(IdentityServerConfig.ApiScopes)
    .AddInMemoryApiResources(IdentityServerConfig.ApiResources)
    .AddInMemoryClients(IdentityServerConfig.Clients)
    .AddResourceOwnerValidator<LocalUserValidator>()
    .AddProfileService<LocalProfileService>();

// --- JWT bearer (protects /auth/me local endpoint) ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = issuerUri;
        options.Audience = "dairy-bidding-api";
        options.RequireHttpsMetadata = false;
    });

builder.Services.AddAuthorization();

// --- CORS ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type")));

// --- Observability ---
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
        tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-identity-service"))
               .AddAspNetCoreInstrumentation()
               .AddOtlpExporter(otlp =>
               {
                   otlp.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                   otlp.Protocol = OtlpExportProtocol.Grpc;
               }))
    .WithMetrics(metrics =>
        metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-identity-service"))
               .AddAspNetCoreInstrumentation()
               .AddRuntimeInstrumentation()
               .AddPrometheusExporter());

var app = builder.Build();

await app.MigrateAndSeedAsync();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
    app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseIdentityServer();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));

app.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    var name = user.Identity?.Name ?? "unknown";
    var claims = user.Claims.Select(c => new { c.Type, c.Value });
    return Results.Ok(new { name, claims });
}).RequireAuthorization();

app.MapPrometheusScrapingEndpoint();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }