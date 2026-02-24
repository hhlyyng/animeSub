using Microsoft.EntityFrameworkCore.Migrations;

namespace backend.Data.Migrations;

/// <summary>
/// Add ImageLandscapes field to store up to 5 TMDB backdrop URLs as a JSON array
/// </summary>
public class AddImageLandscapesToAnimeInfo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            table: "AnimeInfo",
            name: "ImageLandscapes",
            type: "TEXT",
            nullable: true,
            schema: "public"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            table: "AnimeInfo",
            name: "ImageLandscapes",
            schema: "public"
        );
    }
}
