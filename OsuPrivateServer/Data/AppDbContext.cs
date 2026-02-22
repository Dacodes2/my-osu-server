using Microsoft.EntityFrameworkCore;
using OsuPrivateServer.Models;

namespace OsuPrivateServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Score> Scores { get; set; }
    public DbSet<UserRelation> UserRelations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .OwnsOne(u => u.Statistics, s =>
            {
                s.OwnsOne(st => st.Level);
                s.OwnsOne(st => st.GradeCounts);
            });
            
        modelBuilder.Entity<UserRelation>()
            .HasIndex(ur => new { ur.UserId, ur.TargetId })
            .IsUnique();
    }
}
