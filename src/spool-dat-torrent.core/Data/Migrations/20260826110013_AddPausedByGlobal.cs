using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpoolDatTorrent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPausedByGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PausedByGlobal",
                table: "Streams",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PausedByGlobal",
                table: "Streams");
        }
    }
}
