using Microsoft.EntityFrameworkCore;

namespace DairyBidding.AuctionService.Data;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<Auction> Auctions => Set<Auction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Auction>(e =>
        {
            e.ToTable("auctions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.StartingPrice).HasColumnType("numeric(18,2)");
            e.Property(x => x.StartsAt).IsRequired();
            e.Property(x => x.EndsAt).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
        });
    }
}
