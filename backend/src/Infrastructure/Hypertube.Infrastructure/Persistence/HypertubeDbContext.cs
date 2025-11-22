using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Hypertube.Domain.Entities;

namespace Hypertube.Infrastructure.Persistence;

public class HypertubeDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public HypertubeDbContext(DbContextOptions<HypertubeDbContext> options)
        : base(options)
    {
    }

    // DbSet<User> Users is inherited from IdentityDbContext
    public DbSet<Movie> Movies { get; set; } = null!;
    public DbSet<Comment> Comments { get; set; } = null!;
    public DbSet<OAuthProvider> OAuthProviders { get; set; } = null!;
    public DbSet<Torrent> Torrents { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // IMPORTANT: Call base to configure Identity tables

        // User configuration - Only custom properties (Identity handles base properties)
        modelBuilder.Entity<User>(entity =>
        {
            // Identity already configures: Id, UserName, Email, PasswordHash, etc.
            // We only configure our custom properties
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.PreferredLanguage).HasMaxLength(5).HasDefaultValue("en");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        // Movie configuration
        modelBuilder.Entity<Movie>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ImdbId).IsUnique();
            entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ImdbId).HasMaxLength(20);
            entity.Property(e => e.Rating).HasColumnType("decimal(3,1)");
            entity.Property(e => e.Genre).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
        });

        // Comment configuration
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Comments)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.Comments)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // OAuthProvider configuration
        modelBuilder.Entity<OAuthProvider>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ProviderUserId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.OAuthProviders)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Torrent configuration
        modelBuilder.Entity<Torrent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MagnetLink).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("pending");
            entity.Property(e => e.Progress).HasColumnType("decimal(5,2)").HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.Torrents)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).HasMaxLength(500).IsRequired();
            entity.HasIndex(e => e.Token).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
