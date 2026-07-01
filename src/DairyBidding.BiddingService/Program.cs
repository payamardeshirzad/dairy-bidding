using DairyBidding.BiddingService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddBiddingDatabase(builder.Configuration);
builder.Services.AddBiddingAuthentication(builder.Configuration);
builder.Services.AddBiddingMessaging(builder.Configuration);
builder.Services.AddBiddingCors(builder.Configuration);
builder.Services.AddBiddingHealthChecks(builder.Configuration);
builder.Services.AddBiddingTelemetry(builder.Configuration);

var app = builder.Build();

await app.MigrateAsync();

app.UseCorrelationId();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapBiddingEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
