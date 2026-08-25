using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpoolDatTorrent.Core.Data
{
    /// <summary>
    /// Design-time factory used by the <c>dotnet ef</c> tools to create a
    /// <see cref="SpoolDbContext"/> for scaffolding migrations. The Core library has no
    /// composition root, so this provides the DbContext options that the tools need.
    /// </summary>
    public class SpoolDbContextFactory : IDesignTimeDbContextFactory<SpoolDbContext>
    {
        public SpoolDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<SpoolDbContext>()
                .UseSqlite("DataSource=spooldattorrent.db")
                .Options;

            return new SpoolDbContext(options);
        }
    }
}
