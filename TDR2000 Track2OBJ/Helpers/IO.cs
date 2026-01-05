using System;
using System.IO;

namespace TdrExport.Helpers
{
    public static class IO
    {
        public static void LoopDirectoriesIn(string sPath, Action<DirectoryInfo> callBack)
        {
            callBack(new DirectoryInfo(sPath));

            foreach (DirectoryInfo d in new DirectoryInfo(sPath).GetDirectories()) { LoopDirectoriesIn(d.FullName, callBack); }
        }
    }
}
