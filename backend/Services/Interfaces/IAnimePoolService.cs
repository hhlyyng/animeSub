using backend.Models.Dtos;

namespace backend.Services.Interfaces;

/// <summary>
/// Service for the pre-built random anime recommendation pool
/// </summary>
public interface IAnimePoolService
{
    /// <summary>
    /// Number of items currently in the pool (0 if not yet built)
    /// </summary>
    int PoolSize { get; }

    /// <summary>
    /// Whether the pool is currently being built
    /// </summary>
    bool IsBuilding { get; }

    /// <summary>
    /// Get a random selection of anime from the pool
    /// </summary>
    /// <param name="count">Number of anime to return</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Random picks, or empty list if pool is not yet built</returns>
    Task<List<AnimeInfoDto>> GetRandomPicksAsync(int count, CancellationToken ct = default);
}
