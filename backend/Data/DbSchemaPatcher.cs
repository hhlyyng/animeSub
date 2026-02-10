using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

/// <summary>
/// Lightweight runtime schema patcher for SQLite databases created with EnsureCreated.
/// This keeps existing local databases compatible when new columns/indexes are introduced.
/// </summary>
public static class DbSchemaPatcher
{
    public static async Task ApplyAsync(AnimeDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await EnsureColumnAsync(connection, logger, "AnimeInfo", "MikanBangumiId", "TEXT NULL", cancellationToken);

            await EnsureColumnAsync(connection, logger, "DownloadHistory", "Source", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "Progress", "REAL NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "DownloadSpeed", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "Eta", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "NumSeeds", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "NumLeechers", "INTEGER NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "LastSyncedAt", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "SavePath", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, logger, "DownloadHistory", "Category", "TEXT NULL", cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS "MikanFeedCache" (
                    "MikanBangumiId" TEXT NOT NULL CONSTRAINT "PK_MikanFeedCache" PRIMARY KEY,
                    "SeasonName" TEXT NOT NULL,
                    "LatestEpisode" INTEGER NULL,
                    "LatestPublishedAt" TEXT NULL,
                    "LatestTitle" TEXT NULL,
                    "EpisodeOffset" INTEGER NOT NULL DEFAULT 0,
                    "UpdatedAt" TEXT NOT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS "MikanFeedItem" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_MikanFeedItem" PRIMARY KEY AUTOINCREMENT,
                    "MikanBangumiId" TEXT NOT NULL,
                    "Title" TEXT NOT NULL,
                    "TorrentUrl" TEXT NOT NULL DEFAULT '',
                    "MagnetLink" TEXT NOT NULL DEFAULT '',
                    "TorrentHash" TEXT NOT NULL DEFAULT '',
                    "CanDownload" INTEGER NOT NULL DEFAULT 0,
                    "FileSize" INTEGER NOT NULL DEFAULT 0,
                    "FormattedSize" TEXT NOT NULL DEFAULT '',
                    "PublishedAt" TEXT NOT NULL,
                    "Resolution" TEXT NULL,
                    "Subgroup" TEXT NULL,
                    "SubtitleType" TEXT NULL,
                    "Episode" INTEGER NULL,
                    "IsCollection" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT "FK_MikanFeedItem_MikanFeedCache_MikanBangumiId"
                        FOREIGN KEY ("MikanBangumiId") REFERENCES "MikanFeedCache" ("MikanBangumiId") ON DELETE CASCADE
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS "TopAnimeCache" (
                    "Source" TEXT NOT NULL CONSTRAINT "PK_TopAnimeCache" PRIMARY KEY,
                    "PayloadJson" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_AnimeInfo_MikanBangumiId ON AnimeInfo(MikanBangumiId);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_DownloadHistory_Source ON DownloadHistory(Source);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_DownloadHistory_LastSyncedAt ON DownloadHistory(LastSyncedAt);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_MikanFeedCache_UpdatedAt ON MikanFeedCache(UpdatedAt);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_MikanFeedItem_MikanBangumiId ON MikanFeedItem(MikanBangumiId);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_MikanFeedItem_TorrentHash ON MikanFeedItem(TorrentHash);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_MikanFeedItem_PublishedAt ON MikanFeedItem(PublishedAt);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS IX_TopAnimeCache_UpdatedAt ON TopAnimeCache(UpdatedAt);",
                cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task EnsureColumnAsync(
        DbConnection connection,
        ILogger logger,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var exists = await ColumnExistsAsync(connection, table, column, cancellationToken);
        if (exists)
        {
            return;
        }

        var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        await ExecuteNonQueryAsync(connection, sql, cancellationToken);
        logger.LogInformation("Patched schema: added column {Table}.{Column}", table, column);
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader["name"]?.ToString();
            if (string.Equals(columnName, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
