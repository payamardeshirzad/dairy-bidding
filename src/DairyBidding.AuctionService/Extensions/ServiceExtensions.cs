using DairyBidding.AuctionService.Data;
using DairyBidding.AuctionService.Messaging;
using DairyBidding.AuctionService.Messaging.Handlers;
using DairyBidding.Contracts.Events;
using DairyBidding.SharedKernel.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Jwt:Authority"] ?? "http://localhost:5245";
                options.Audience = configuration["Jwt:Audience"] ?? "dairy-bidding-api";
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddAuctionMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<AuctionDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            // ADR-028: consume BidPlacedEvent to update denormalized CurrentPrice/BidCount
            x.AddConsumer<BidPlacedConsumer>();

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

                cfg.Message<AuctionStatusChangedEvent>(e => e.SetEntityName("auctions.fanout"));
                cfg.Publish<AuctionStatusChangedEvent>(p => p.ExchangeType = "fanout");

                // Bind to the existing bids.topic exchange published by BiddingService
                cfg.Message<BidPlacedEvent>(e => e.SetEntityName("bids.topic"));

                cfg.ReceiveEndpoint("auction.bid-placed", e =>
                {
                    e.SetQueueArgument("x-queue-type", "quorum");
                    e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromMilliseconds(500)));
                    e.ConfigureConsumer<BidPlacedConsumer>(ctx);
                });
            });
        });

        services.AddScoped<IMessageHandler<BidPlacedEvent>, BidPlacedHandler>();

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
                failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);

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
