using Microsoft.EntityFrameworkCore;

namespace DairyBidding.BiddingService.Data;

public class BiddingDbContext : DbContext
{
    public BiddingDbContext(DbContextOptions<BiddingDbContext> options) : base(options) { }

    public DbSet<Bid> Bids => Set<Bid>();

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
        });
    }
}

public class Bid
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AuctionId { get; set; } = default!;
    public string BidderId { get; set; } = default!;
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}