using System.Text;
using DairyBidding.BiddingService.Data;
using DairyBidding.BiddingService.Messaging;
using DairyBidding.BiddingService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<BiddingDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.AddConsumer<BidPlacedConsumer>();
            x.AddConsumer<AuctionStatusChangedConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = configuration["RabbitMQ:Host"] ?? "localhost";
                var port = configuration["RabbitMQ:Port"] ?? "5672";
                var vhost = configuration["RabbitMQ:VirtualHost"] ?? "/";

                cfg.Host(new Uri($"rabbitmq://{host}:{port}/{Uri.EscapeDataString(vhost)}"), h =>
                {
                    h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                    h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                });

                cfg.Message<BidPlacedEvent>(e => e.SetEntityName("bids.topic"));
                cfg.Publish<BidPlacedEvent>(p => p.ExchangeType = "topic");

                cfg.Message<AuctionStatusChangedEvent>(e => e.SetEntityName("auctions.fanout"));
                cfg.Publish<AuctionStatusChangedEvent>(p => p.ExchangeType = "fanout");

                cfg.ReceiveEndpoint("bidding.bid-placed", e =>
                {
                    e.SetQueueArgument("x-queue-type", "quorum");
                    e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromMilliseconds(500)));
                    e.ConfigureConsumer<BidPlacedConsumer>(ctx);
                });

                cfg.ReceiveEndpoint("bidding.auction-status-changed", e =>
                {
                    e.SetQueueArgument("x-queue-type", "quorum");
                    e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromMilliseconds(500)));
                    e.ConfigureConsumer<AuctionStatusChangedConsumer>(ctx);
                });
            });
        });

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
                failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);

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
