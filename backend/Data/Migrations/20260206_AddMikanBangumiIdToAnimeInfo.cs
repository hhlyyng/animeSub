using Microsoft.EntityFrameworkCore.Migrations;

namespace backend.Data.Migrations;

/// <summary>
/// Add MikanBangumiId field to support Mikan RSS feeds
/// </summary>
public class AddMikanBangumiIdToAnimeInfo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            table: "AnimeInfo",
            name: "MikanBangumiId",
            type: "TEXT",
            nullable: true,
            schema: "public"
        );
    }
}
