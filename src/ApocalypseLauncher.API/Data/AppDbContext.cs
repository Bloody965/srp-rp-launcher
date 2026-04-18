using Microsoft.EntityFrameworkCore;
using ApocalypseLauncher.API.Models;

namespace ApocalypseLauncher.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<LoginSession> LoginSessions { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<ModpackVersion> ModpackVersions { get; set; }
    public DbSet<PlayerSkin> PlayerSkins { get; set; }
    public DbSet<PlayerCape> PlayerCapes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.MinecraftUUID)
            .IsUnique();

        // LoginSession indexes
        modelBuilder.Entity<LoginSession>()
            .HasIndex(s => s.Token)
            .IsUnique();

        modelBuilder.Entity<LoginSession>()
            .HasIndex(s => s.UserId);

        // AuditLog indexes
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.CreatedAt);

        // ModpackVersion indexes
        modelBuilder.Entity<ModpackVersion>()
            .HasIndex(m => m.Version)
            .IsUnique();

        modelBuilder.Entity<ModpackVersion>()
            .HasIndex(m => m.IsActive);

        // PlayerSkin indexes
        modelBuilder.Entity<PlayerSkin>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<PlayerSkin>()
            .HasIndex(s => s.IsActive);

        // PlayerCape indexes
        modelBuilder.Entity<PlayerCape>()
            .HasIndex(c => c.UserId);

        modelBuilder.Entity<PlayerCape>()
            .HasIndex(c => c.IsActive);

        // Relationships
        modelBuilder.Entity<LoginSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PlayerSkin>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlayerCape>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
