using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpoolDatTorrent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamSpoolingCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SpoolingCapGb",
                table: "Streams",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpoolingCapGb",
                table: "Streams");
        }
    }
}
