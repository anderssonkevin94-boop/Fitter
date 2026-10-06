using System;
using System.Security.Cryptography;
using System.Text;

namespace Ravenholm.Infrastructure.Save
{
    /// <summary>
    /// Wraps a JSON body with a one-line header carrying its byte length and SHA-256.
    /// This detects truncation and accidental corruption; it is not anti-cheat.
    ///
    ///   RAVENHOLM-SAVE 1 bytes=1234 sha256=ab12...\n{ ...json... }
    /// </summary>
    public static class SaveFileCodec
    {
        public const string Magic = "RAVENHOLM-SAVE";
        public const int FormatVersion = 1;

        public static string Encode(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            return $"{Magic} {FormatVersion} bytes={body.Length} sha256={Sha256Hex(body)}\n{json}";
        }

        public static bool TryDecode(string text, out string json, out string error)
        {
            json = null;
            if (string.IsNullOrEmpty(text)) { error = "file is empty"; return false; }

            var newline = text.IndexOf('\n');
            if (newline < 0) { error = "header line missing (file truncated?)"; return false; }

            var header = text.Substring(0, newline).Split(' ');
            if (header.Length != 4 || header[0] != Magic) { error = "not a Ravenholm save file"; return false; }
            if (header[1] != FormatVersion.ToString()) { error = $"unsupported file format {header[1]}"; return false; }
            if (!header[2].StartsWith("bytes=", StringComparison.Ordinal) || !int.TryParse(header[2].Substring(6), out var expectedBytes))
            { error = "malformed byte count"; return false; }
            if (!header[3].StartsWith("sha256=", StringComparison.Ordinal)) { error = "malformed checksum"; return false; }

            var body = text.Substring(newline + 1);
            var bytes = Encoding.UTF8.GetBytes(body);
            if (bytes.Length != expectedBytes) { error = $"length mismatch: expected {expectedBytes} bytes, found {bytes.Length} (truncated?)"; return false; }
            if (!string.Equals(Sha256Hex(bytes), header[3].Substring(7), StringComparison.Ordinal)) { error = "checksum mismatch (corrupted)"; return false; }

            json = body;
            error = null;
            return true;
        }

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
