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
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(100);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
            e.Property(x => x.StartingPrice).HasColumnName("startingprice").HasColumnType("numeric(18,2)");
            e.Property(x => x.StartsAt).HasColumnName("startsat").IsRequired();
            e.Property(x => x.EndsAt).HasColumnName("endsat").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("createdatutc").IsRequired();
        });
    }
}
