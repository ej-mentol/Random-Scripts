using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using TdrExport.Helpers;

namespace TdrExport.TDR2000.Formats
{
    public class H
    {
        public Dictionary<int, string> Definitions { get; set; } = new Dictionary<int, string>();

        public static H Load(string path)
        {
            using (StreamReader sr = new StreamReader(path))
            {
                return LoadFromStream(sr);
            }
        }

        public static H LoadFromStream(StreamReader sr)
        {
            H h = new H();

            string[] lines = sr.ReadToEnd().Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#define"))
                {
                    string[] parts = lines[i].Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                    h.Definitions.Add(int.Parse(parts[2]), parts[1]);
                }
            }

            return h;
        }

        public void Save(string path)
        {
            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine();
                sw.WriteLine($"// Node identifiers for {Path.GetFileNameWithoutExtension(path)} hierarchy");
                sw.WriteLine();

                foreach (KeyValuePair<int, string> kvp in Definitions)
                {
                    sw.WriteLine($"#define {kvp.Value}        {kvp.Key}");
                }

                sw.WriteLine();
            }
        }
    }
}