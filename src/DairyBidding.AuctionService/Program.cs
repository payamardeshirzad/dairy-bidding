using DairyBidding.AuctionService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddAuctionDatabase(builder.Configuration);
builder.Services.AddAuctionOptions(builder.Configuration);
builder.Services.AddAuctionAuthentication(builder.Configuration);
builder.Services.AddAuctionMessaging(builder.Configuration);
builder.Services.AddAuctionCors(builder.Configuration);
builder.Services.AddAuctionHealthChecks(builder.Configuration);
builder.Services.AddAuctionTelemetry(builder.Configuration);

var app = builder.Build();

await app.MigrateAsync();

app.UseCors("Frontend");
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.SeedDevDataOnStarted();

app.MapAuctionEndpoints();

app.Run();

public partial class Program { }
