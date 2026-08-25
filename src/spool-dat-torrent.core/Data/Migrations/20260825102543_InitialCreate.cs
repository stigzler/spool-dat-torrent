using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpoolDatTorrent.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Streams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TorrentIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DatFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalTorrentPath = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalMagnet = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalDatPath = table.Column<string>(type: "TEXT", nullable: true),
                    CachedTorrentPath = table.Column<string>(type: "TEXT", nullable: true),
                    CachedDatPath = table.Column<string>(type: "TEXT", nullable: true),
                    SpoolingTargetOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Strategy = table.Column<int>(type: "INTEGER", nullable: false),
                    FileFilter = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ServerProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    MovedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMatchedByDat = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSelectedForDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TorrentStreamItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_Streams_TorrentStreamItemId",
                        column: x => x.TorrentStreamItemId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_FileIndex",
                table: "Files",
                column: "FileIndex");

            migrationBuilder.CreateIndex(
                name: "IX_Files_Status",
                table: "Files",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Files_TorrentStreamItemId",
                table: "Files",
                column: "TorrentStreamItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_TorrentIdentifier",
                table: "Streams",
                column: "TorrentIdentifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "Streams");
        }
    }
}
