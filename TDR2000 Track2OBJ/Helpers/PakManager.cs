using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TdrExport.Helpers;

namespace TdrExport.Helpers
{
    public class PakManager
    {
        public class FileEntry
        {
            public string Name;
            public string PakPath;
            public uint Offset;
            public uint Size;
        }

        private Dictionary<string, FileEntry> _index = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        private const byte FlagFile = 0x08;
        private const byte FlagBranch = 0x40;
        private const byte FlagSibling = 0x80;

        public void IndexDirectory(string rootPath)
        {
            Console.WriteLine($"Scanning for archives in: {rootPath}");
            var dirFiles = Directory.GetFiles(rootPath, "*.dir", SearchOption.AllDirectories);
            foreach (var dirFile in dirFiles)
            {
                string pakPath = Path.ChangeExtension(dirFile, ".pak");
                if (File.Exists(pakPath))
                {
                    ParseTrieIndex(dirFile, pakPath);
                }
            }
            Console.WriteLine($"VFS Index built: {_index.Count} files tracked.");
        }

        private void ParseTrieIndex(string dirPath, string pakPath)
        {
            byte[] data = File.ReadAllBytes(dirPath);
            int pos = 0;

            void Walk(string prefix)
            {
                while (pos < data.Length)
                {
                    if (pos + 2 > data.Length) break;

                    byte chr = data[pos];
                    byte flags = data[pos + 1];
                    pos += 2;

                    string currentName = prefix + (char)chr;

                    if ((flags & FlagFile) != 0)
                    {
                        if (pos + 8 > data.Length) break;

                        uint offset = BitConverter.ToUInt32(data, pos);
                        uint size = BitConverter.ToUInt32(data, pos + 4);
                        pos += 8;

                        string fileName = Path.GetFileName(currentName);
                        if (!_index.ContainsKey(fileName))
                        {
                            _index[fileName] = new FileEntry { 
                                Name = currentName, 
                                PakPath = pakPath, 
                                Offset = offset, 
                                Size = size 
                            };
                        }
                    }

                    if ((flags & FlagBranch) != 0) Walk(currentName);
                    if ((flags & FlagSibling) == 0) break;
                }
            }

            Walk(string.Empty);
        }

        public byte[] LoadFile(string fileName)
        {
            // 1. Try real disk first (in some search root, handle externally or here)
            // 2. Try VFS
            if (_index.TryGetValue(Path.GetFileName(fileName), out var entry))
            {
                using var fs = new FileStream(entry.PakPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] raw = new byte[entry.Size];
                fs.ReadExactly(raw, 0, (int)entry.Size);
                return DecompressZig(raw);
            }
            return null;
        }

        private static byte RotateRight8(byte value, int count)
        {
            int c = count & 7;
            return (byte)((value >> c) | (value << (8 - c)));
        }

        public static byte[] DecompressZig(byte[] raw)
        {
            if (raw == null || raw.Length < 8) return raw;

            byte key = raw[0];
            byte[] sigBytes = new byte[3];
            for (int i = 0; i < 3; i++) sigBytes[i] = (byte)(raw[1 + i] ^ key);

            string signature = Encoding.ASCII.GetString(sigBytes);

            if (signature == "RAW")
            {
                byte[] result = new byte[raw.Length - 8];
                Buffer.BlockCopy(raw, 8, result, 0, result.Length);
                return result;
            }

            if (signature == "zIG")
            {
                try
                {
                    // zIG uses ZLib (deflate with header)
                    // .NET ZLibStream expects the ZLib header (CMF/FLG)
                    using var ms = new MemoryStream(raw, 8, raw.Length - 8);
                    using var z = new ZLibStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    z.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch { return raw; }
            }

            return raw;
        }

        public bool FileExists(string name) => _index.ContainsKey(Path.GetFileName(name));

        public List<FileEntry> GetFiles() => _index.Values.ToList();
    }
}
