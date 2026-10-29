using Microsoft.EntityFrameworkCore;
using techHiveBackend.Models;

namespace techHiveBackend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();
        user.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        user.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        user.Property(u => u.Email).HasMaxLength(254).UseCollation("NOCASE").IsRequired();
        user.HasIndex(u => u.Email).IsUnique();
    }
}
