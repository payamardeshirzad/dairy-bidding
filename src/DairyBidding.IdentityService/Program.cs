using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using DairyBidding.IdentityService.Data;
using DairyBidding.IdentityService.Extensions;
using DairyBidding.IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// --- JWT ---
var jwtSection = builder.Configuration.GetSection("Jwt");
var issuer = jwtSection["Issuer"] ?? "dairy-identity";
var audience = jwtSection["Audience"] ?? "dairy-bidding-api";
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is required. Set it via dotnet user-secrets.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<JwtBearerEvents>>();
                logger.LogWarning("JWT authentication failed: {Message}", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// --- CORS ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type")));

// --- Rate limiting ---
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("token-endpoint", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

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

app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));

app.MapPost("/auth/token", async (LoginRequest request, IUserService users, ITokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    var user = await users.ValidateCredentialsAsync(request.Username, request.Password);
    if (user is null)
        return Results.Unauthorized();

    var (jwt, expiresAt) = tokens.CreateToken(user.Username, user.Role);
    return Results.Ok(new TokenResponse(jwt, "Bearer", expiresAt));
}).RequireRateLimiting("token-endpoint");

app.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    var name = user.Identity?.Name ?? "unknown";
    var claims = user.Claims.Select(c => new { c.Type, c.Value });
    return Results.Ok(new { name, claims });
}).RequireAuthorization();

app.MapPrometheusScrapingEndpoint();

app.Run();

record LoginRequest(string Username, string Password);
record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);