using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DairyBidding.AuctionService.Data;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<AuctionExtension> AuctionExtensions => Set<AuctionExtension>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Auction>(e =>
        {
            e.ToTable("auctions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.StartingPrice).HasPrecision(18, 4);
            e.Property(x => x.StartsAt).IsRequired();
            e.Property(x => x.EndsAt).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
            // ADR-028: denormalized bid counters
            e.Property(x => x.CurrentPrice).HasPrecision(18, 4).HasDefaultValue(0m);
            e.Property(x => x.BidCount).HasDefaultValue(0);
            // ADR-022: optimistic concurrency token
            e.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0);
            // ADR-040: extension count, co-located with RowVersion for atomic update under optimistic lock
            e.Property(x => x.ExtensionCount).HasDefaultValue(0);
        });

        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProcessedAtUtc).IsRequired();
            e.HasIndex(x => x.MessageId).IsUnique();
        });

        modelBuilder.Entity<AuctionExtension>(e =>
        {
            e.ToTable("auction_extensions");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuctionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.BidId).IsRequired();
            e.Property(x => x.PreviousEnd).IsRequired();
            e.Property(x => x.NewEnd).IsRequired();
            e.Property(x => x.ExtendedAt).IsRequired();
            // One bid can trigger at most one extension; also acts as idempotency safety net
            e.HasIndex(x => x.BidId).IsUnique();
        });

        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddInboxStateEntity();
    }
}
