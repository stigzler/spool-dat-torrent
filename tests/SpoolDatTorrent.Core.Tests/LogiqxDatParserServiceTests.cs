using System.IO;
using System.Text;
using System.Threading.Tasks;
using SpoolDatTorrent.Core.Services;
using Xunit;

namespace SpoolDatTorrent.Core.Tests
{
    public class LogiqxDatParserServiceTests
    {
        private const string SampleRedumpXml = @"<?xml version=""1.0""?>
        <datafile>
            <header>
                <name>turbografx-cd</name>
                <description>NEC - PC Engine CD</description>
            </header>
            <game name=""1552 Tenka Tairan (Japan)"">
                <description>1552 Tenka Tairan (Japan)</description>
                <rom name=""1552 Tenka Tairan (Japan) (Track 01).bin"" size=""7916832"" crc=""22144d0f"" />
                <rom name=""1552 Tenka Tairan (Japan) (Track 02).bin"" size=""33694752"" crc=""160d1887"" />
            </game>
            <game name=""3x3 Eyes - Sanjiyan Henjou (Japan) (Rev 1)"">
                <description>3x3 Eyes - Sanjiyan Henjou (Japan) (Rev 1)</description>
                <rom name=""3x3 Eyes - Sanjiyan Henjou (Japan) (Rev 1) (Track 1).bin"" size=""3048192"" crc=""1e75a8a5"" />
            </game>
        </datafile>";

        [Fact]
        public async Task GetGameNamesFromStreamAsync_ValidXml_ReturnsUniqueGameNames()
        {
            var service = new LogiqxDatParserService();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleRedumpXml));

            var gameNames = await service.GetGameNamesFromStreamAsync(stream);

            Assert.Equal(2, gameNames.Count);
            Assert.Contains("1552 Tenka Tairan (Japan)", gameNames);
            Assert.Contains("3x3 Eyes - Sanjiyan Henjou (Japan) (Rev 1)", gameNames);
        }

        [Fact]
        public async Task GetGameNamesFromStreamAsync_ReturnsCaseInsensitiveHashSet()
        {
            var service = new LogiqxDatParserService();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleRedumpXml));

            var gameNames = await service.GetGameNamesFromStreamAsync(stream);

            // Verify the OrdinalIgnoreCase string comparer is working properly
            Assert.Contains("1552 TENKA TAIRAN (JAPAN)", gameNames);
            Assert.Contains("3x3 eyes - sanjiyan henjou (japan) (rev 1)", gameNames);
            Assert.DoesNotContain("NonExistent Game", gameNames);
        }
    }
}