using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// Supported BitTorrent client types. Add new members here in a single place so every
    /// host (web, CLI, desktop) stays in sync. Only qBittorrent is implemented today.
    /// </summary>
    public enum BitTorrentClientType
    {
        QBittorrent
    }

    /// <summary>
    /// JSON converter for <see cref="BitTorrentClientType"/>. Serializes as a string so
    /// config.json stays human-readable, and reads the string back to the enum member
    /// (case-insensitive).
    /// </summary>
    public sealed class BitTorrentClientTypeConverter : JsonConverter<BitTorrentClientType>
    {
        public override BitTorrentClientType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            foreach (BitTorrentClientType member in Enum.GetValues<BitTorrentClientType>())
            {
                if (string.Equals(member.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return member;
                }
            }

            throw new JsonException($"Unknown BitTorrent client type: '{value}'.");
        }

        public override void Write(Utf8JsonWriter writer, BitTorrentClientType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
