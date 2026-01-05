using System.Collections.Generic;
using System.Linq;

namespace TdrExport.Helpers
{
    public abstract class Material
    {
        protected Dictionary<string, string> fileNames = new Dictionary<string, string>();

        public virtual string FileName => "";

        public virtual List<string> FileNames => fileNames.Values.ToList();

        public string GetFile(string key)
        {
            fileNames.TryGetValue(key, out string fileName);

            return fileName;
        }
    }
}
