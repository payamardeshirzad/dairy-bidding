using System.Text;
using DairyBidding.BiddingService.Data;
using DairyBidding.BiddingService.Messaging;
using DairyBidding.BiddingService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace DairyBidding.BiddingService.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddBiddingDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddDbContext<BiddingDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        return services;
    }

    public static IServiceCollection AddBiddingAuthentication(this IServiceCollection services, IConfiguration configuration)
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

    public static IServiceCollection AddBiddingMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMQ"));
        services.AddSingleton<BidPlacedPublisher>();
        services.AddSingleton<IBidPlacedPublisher>(sp => sp.GetRequiredService<BidPlacedPublisher>());
        services.AddHostedService(sp => sp.GetRequiredService<BidPlacedPublisher>());
        services.AddHostedService<BidPlacedConsumer>();
        services.AddHostedService<AuctionStatusChangedConsumer>();

        services.AddScoped<IMessageHandler<BidPlacedEvent>, BidPlacedHandler>();
        services.AddScoped<IMessageHandler<AuctionStatusChangedEvent>, AuctionStatusChangedHandler>();

        return services;
    }

    public static IServiceCollection AddBiddingCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
            options.AddPolicy("Frontend", policy =>
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "OPTIONS")
                      .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")));
        return services;
    }

    public static IServiceCollection AddBiddingHealthChecks(this IServiceCollection services, IConfiguration configuration)
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

    public static IServiceCollection AddBiddingTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
                tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-bidding-service"))
                       .AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddOtlpExporter(otlp =>
                       {
                           otlp.Endpoint = new Uri(configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
                           otlp.Protocol = OtlpExportProtocol.Grpc;
                       }))
            .WithMetrics(metrics =>
                metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("dairy-bidding-service"))
                       .AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddPrometheusExporter());
        return services;
    }
}
