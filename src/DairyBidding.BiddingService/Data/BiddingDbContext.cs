using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Data;

public class BiddingDbContext : DbContext
{
    public BiddingDbContext(DbContextOptions<BiddingDbContext> options) : base(options) { }

    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bid>(e =>
        {
            e.ToTable("bids");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AuctionId).HasColumnName("auctionid").HasMaxLength(100).IsRequired();
            e.Property(x => x.BidderId).HasColumnName("bidderid").HasMaxLength(100).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("createdatutc").IsRequired();
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotencykey").HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.BidderId, x.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MessageId).HasColumnName("messageid").HasMaxLength(100).IsRequired();
            e.Property(x => x.ProcessedAtUtc).HasColumnName("processedatutc").IsRequired();
        });
    }
}
