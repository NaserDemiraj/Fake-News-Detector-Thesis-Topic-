using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FakeNewsDetector.Migrations
{
    public partial class AddExtendedFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "SavedAnalyses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "SavedAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "SavedAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "SavedAnalyses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "SavedAnalyses",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ContentType", table: "SavedAnalyses");
            migrationBuilder.DropColumn(name: "Content", table: "SavedAnalyses");
            migrationBuilder.DropColumn(name: "ResultJson", table: "SavedAnalyses");
            migrationBuilder.DropColumn(name: "IsFavorite", table: "SavedAnalyses");
            migrationBuilder.DropColumn(name: "Notes", table: "SavedAnalyses");
        }
    }
}
