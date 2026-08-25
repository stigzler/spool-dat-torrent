using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpoolDatTorrent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamSettlingTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SettlingTimeSeconds",
                table: "Streams",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettlingTimeSeconds",
                table: "Streams");
        }
    }
}
