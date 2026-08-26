using System;
using System.Security.Cryptography;
using System.Text;

namespace SpoolDatTorrent.Core.Configuration
{
    /// <summary>
    /// Abstraction over secret protection so the storage format can be swapped per platform.
    /// The default implementation uses AES-GCM with a key from the SDT_SECRET_KEY environment
    /// variable, which works on both Windows and Linux (Docker).
    /// </summary>
    public interface ISecretProtector
    {
        /// <summary>Encrypt a plaintext secret for storage. Returns a string with a scheme prefix.</summary>
        string Protect(string plaintext);

        /// <summary>Decrypt a stored secret. Handles plaintext (no prefix) for backward compatibility.</summary>
        string Unprotect(string stored);
    }

    /// <summary>
    /// AES-GCM implementation. Encrypted values are stored as "aes:&lt;base64&gt;". The key is
    /// read from the SDT_SECRET_KEY environment variable (or a mounted secret file path given
    /// in SDT_SECRET_KEY_FILE). If no key is configured, secrets are stored as plaintext so the
    /// app still works (with a warning) — set the key to enable encryption.
    /// </summary>
    public class AesSecretProtector : ISecretProtector
    {
        private const string Prefix = "aes:";
        private const string EnvKey = "SDT_SECRET_KEY";
        private const string EnvKeyFile = "SDT_SECRET_KEY_FILE";
        private readonly byte[]? _key;

        public AesSecretProtector()
        {
            _key = LoadKey();
        }

        private static byte[]? LoadKey()
        {
            string? key = Environment.GetEnvironmentVariable(EnvKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                var keyFile = Environment.GetEnvironmentVariable(EnvKeyFile);
                if (!string.IsNullOrWhiteSpace(keyFile) && File.Exists(keyFile))
                {
                    key = File.ReadAllText(keyFile).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return null; // No key configured — fall back to plaintext.
            }

            // Accept a raw string (hashed to 32 bytes) or a base64 32-byte key.
            if (key.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.FromBase64String(key.Substring("base64:".Length));
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes(key));
        }

        public string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                return string.Empty;
            }

            if (_key == null)
            {
                // No key configured — store plaintext so the app still works.
                return plaintext;
            }

            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            var tag = new byte[16];
            var cipher = new byte[plainBytes.Length];

            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, plainBytes, cipher, tag);

            // Format: nonce || tag || cipher
            var payload = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);

            return Prefix + Convert.ToBase64String(payload);
        }

        public string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return string.Empty;
            }

            // Plaintext (legacy config or no key configured) — return as-is.
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return stored;
            }

            if (_key == null)
            {
                return string.Empty;
            }

            try
            {
                var payload = Convert.FromBase64String(stored.Substring(Prefix.Length));
                var nonce = new byte[12];
                var tag = new byte[16];
                var cipher = new byte[payload.Length - nonce.Length - tag.Length];

                Buffer.BlockCopy(payload, 0, nonce, 0, nonce.Length);
                Buffer.BlockCopy(payload, nonce.Length, tag, 0, tag.Length);
                Buffer.BlockCopy(payload, nonce.Length + tag.Length, cipher, 0, cipher.Length);

                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(_key, tag.Length);
                aes.Decrypt(nonce, cipher, tag, plain);

                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                // Can't decrypt (wrong key, corrupted) — return empty rather than crash.
                return string.Empty;
            }
        }
    }
}
