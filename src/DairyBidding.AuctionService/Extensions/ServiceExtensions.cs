using System.Text;
using DairyBidding.AuctionService.Data;
using DairyBidding.AuctionService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace DairyBidding.AuctionService.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddAuctionDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddDbContext<AuctionDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        return services;
    }

    public static IServiceCollection AddAuctionAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "dairy-identity";
        var audience = configuration["Jwt:Audience"] ?? "dairy-bidding-api";
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required. Set it via dotnet user-secrets.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAuctionMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMQ"));
        services.AddSingleton<AuctionEventPublisher>();
        services.AddSingleton<IAuctionEventPublisher>(sp => sp.GetRequiredService<AuctionEventPublisher>());
        services.AddHostedService(sp => sp.GetRequiredService<AuctionEventPublisher>());
        return services;
    }

    public static IServiceCollection AddAuctionCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
            options.AddPolicy("Frontend", policy =>
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "OPTIONS")
                      .WithHeaders("Authorization", "Content-Type")));
        return services;
    }

    public static IServiceCollection AddAuctionHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgres",
                failureStatus: HealthStatus.Unhealthy, tags: ["ready"])
            .AddRabbitMQ(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var factory = new ConnectionFactory
                {
                    HostName = cfg["RabbitMQ:Host"] ?? "localhost",
                    Port = int.TryParse(cfg["RabbitMQ:Port"], out var p) ? p : 5672,
                    UserName = cfg["RabbitMQ:User"] ?? "guest",
                    Password = cfg["RabbitMQ:Pass"] ?? "guest",
                    VirtualHost = cfg["RabbitMQ:VHost"] ?? "/",
                };
                return factory.CreateConnectionAsync();
            }, name: "rabbitmq", failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddAuctionTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
                tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-auction-service"))
                       .AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddOtlpExporter(otlp =>
                       {
                           otlp.Endpoint = new Uri(configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                           otlp.Protocol = OtlpExportProtocol.Grpc;
                       }))
            .WithMetrics(metrics =>
                metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-auction-service"))
                       .AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddPrometheusExporter());
        return services;
    }
}
