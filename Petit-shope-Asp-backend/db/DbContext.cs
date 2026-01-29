using Microsoft.EntityFrameworkCore;
using PetitShope.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Business> Businesses { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Page> Pages { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure relationships and cascade behavior
        modelBuilder.Entity<Business>()
            .HasMany(b => b.Pages)
            .WithOne(p => p.Business)
            .HasForeignKey(p => p.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Page>()
            .HasMany(p => p.Carts)
            .WithOne(c => c.Page)
            .HasForeignKey(c => c.PageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
