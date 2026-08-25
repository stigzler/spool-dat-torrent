using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpoolDatTorrent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamPriorityTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DePriorityTerms",
                table: "Streams",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PriorityTerms",
                table: "Streams",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DePriorityTerms",
                table: "Streams");

            migrationBuilder.DropColumn(
                name: "PriorityTerms",
                table: "Streams");
        }
    }
}
