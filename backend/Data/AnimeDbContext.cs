using Microsoft.EntityFrameworkCore;
using backend.Data.Entities;

namespace backend.Data;

/// <summary>
/// Database context for anime caching and subscription management
/// Uses SQLite for persistent storage of anime metadata, images, and subscriptions
/// </summary>
public class AnimeDbContext : DbContext
{
    public DbSet<AnimeInfoEntity> AnimeInfos { get; set; }
    public DbSet<AnimeImagesEntity> AnimeImages { get; set; }
    public DbSet<DailyScheduleCacheEntity> DailyScheduleCaches { get; set; }
    public DbSet<SubscriptionEntity> Subscriptions { get; set; }
    public DbSet<DownloadHistoryEntity> DownloadHistory { get; set; }

    public AnimeDbContext(DbContextOptions<AnimeDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure AnimeInfo
        modelBuilder.Entity<AnimeInfoEntity>(entity =>
        {
            entity.HasKey(e => e.BangumiId);

            // Indexes for common queries
            entity.HasIndex(e => e.Weekday);
            entity.HasIndex(e => new { e.NameChinese, e.NameJapanese });
        });

        // Configure AnimeImages (standalone, no FK constraint to AnimeInfo)
        modelBuilder.Entity<AnimeImagesEntity>(entity =>
        {
            entity.HasKey(e => e.BangumiId);
        });

        // Configure DailyScheduleCache
        modelBuilder.Entity<DailyScheduleCacheEntity>(entity =>
        {
            entity.HasKey(e => e.Date);
        });

        // Configure Subscription
        modelBuilder.Entity<SubscriptionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Indexes for common queries
            entity.HasIndex(e => e.BangumiId);
            entity.HasIndex(e => e.MikanBangumiId);
            entity.HasIndex(e => e.IsEnabled);

            // Configure relationship with DownloadHistory
            entity.HasMany(e => e.DownloadHistory)
                .WithOne(d => d.Subscription)
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DownloadHistory
        modelBuilder.Entity<DownloadHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique index on TorrentHash to prevent duplicate downloads
            entity.HasIndex(e => e.TorrentHash).IsUnique();

            // Index for queries
            entity.HasIndex(e => e.SubscriptionId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.LastSyncedAt);
        });
    }
}
