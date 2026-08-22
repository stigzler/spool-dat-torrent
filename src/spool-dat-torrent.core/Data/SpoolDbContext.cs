using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SpoolDatTorrent.Core.Models;

namespace SpoolDatTorrent.Core.Data
{
    public class SpoolDbContext : DbContext
    {
        public DbSet<TorrentStreamItem> Streams => Set<TorrentStreamItem>();
        public DbSet<TorrentFileItem> Files => Set<TorrentFileItem>();

        public SpoolDbContext(DbContextOptions<SpoolDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TorrentStreamItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TorrentIdentifier);
                entity.HasMany(e => e.Files)
                      .WithOne()
                      .HasForeignKey("TorrentStreamItemId")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TorrentFileItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property<int>("TorrentStreamItemId");
                entity.HasIndex(e => e.FileIndex);
                entity.HasIndex(e => e.Status);
            });
        }
    }
}