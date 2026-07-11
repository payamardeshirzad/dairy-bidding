using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Data;

public class BiddingDbContext : DbContext
{
    public BiddingDbContext(DbContextOptions<BiddingDbContext> options) : base(options) { }

    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<AuctionBidReadModel> AuctionBidReadModels => Set<AuctionBidReadModel>();
    public DbSet<AuctionReadModel> AuctionReadModels => Set<AuctionReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bid>(e =>
        {
            e.ToTable("bids");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuctionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.BidderId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)").IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.BidderId, x.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProcessedAtUtc).IsRequired();
            e.HasIndex(x => x.MessageId).IsUnique();
        });

        modelBuilder.Entity<AuctionBidReadModel>(e =>
        {
            e.ToTable("auction_bid_read_models");
            e.HasKey(x => x.AuctionId);
            e.Property(x => x.AuctionId).HasMaxLength(100);
            e.Property(x => x.HighestBidAmount).HasPrecision(18, 4);
            e.Property(x => x.HighestBidderId).IsRequired().HasMaxLength(100);
            // ADR-022: optimistic concurrency token — EF includes row_version in UPDATE WHERE clause
            e.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0);
        });

        modelBuilder.Entity<AuctionReadModel>(e =>
        {
            e.ToTable("auction_read_models");
            e.HasKey(x => x.AuctionId);
            e.Property(x => x.AuctionId).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.StartingPrice).HasPrecision(18, 4);
            e.Property(x => x.StartsAt).IsRequired();
            e.Property(x => x.EndsAt).IsRequired();
            e.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
    }
}