using Microsoft.EntityFrameworkCore;
using backend.Data.Entities;

namespace backend.Data;

/// <summary>
/// Database context for anime caching
/// Uses SQLite for persistent storage of anime metadata and images
/// </summary>
public class AnimeDbContext : DbContext
{
    public DbSet<AnimeInfoEntity> AnimeInfos { get; set; }
    public DbSet<AnimeImagesEntity> AnimeImages { get; set; }
    public DbSet<DailyScheduleCacheEntity> DailyScheduleCaches { get; set; }

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
    }
}
