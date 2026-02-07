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
