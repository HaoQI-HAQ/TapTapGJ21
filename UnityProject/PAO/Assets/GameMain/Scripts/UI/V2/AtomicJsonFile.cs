using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PAO.UI.V2
{
    // A completed write replaces the original only after the new bytes reach disk.
    public static class AtomicJsonFile
    {
        [Serializable]
        private sealed class Envelope
        {
            public int format = 1;
            public string payload;
            public string checksum;
        }

        public static void Write(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            string content = JsonUtility.ToJson(new Envelope { payload = json, checksum = Hash(json) });
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        public static string Read(string path)
        {
            string content = File.ReadAllText(path, Encoding.UTF8);
            if (!content.Contains("\"payload\"") && !content.Contains("\"checksum\"")) return content;
            Envelope envelope = JsonUtility.FromJson<Envelope>(content);
            if (envelope == null) throw new InvalidDataException("记录内容为空。");
            // V1 saves used plain JSON. Leave their bytes intact until the next explicit save.
            if (envelope.format != 1 || string.IsNullOrEmpty(envelope.payload) ||
                !string.Equals(envelope.checksum, Hash(envelope.payload), StringComparison.Ordinal))
                throw new InvalidDataException("记录校验失败。");
            return envelope.payload;
        }

        private static string Hash(string value)
        {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}
