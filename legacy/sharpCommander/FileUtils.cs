using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace sharpCommander
{
    static class FileUtils
    {
        public enum EventType
        {
            Init,
            End
        }

        public delegate void updaterCallBack(string file, EventType eventType);

        public static void Copy(string source, string destination, bool overwrite , updaterCallBack updater)
        {
            if (File.Exists(source))
            {
                if (!Directory.Exists(destination))
                    Directory.CreateDirectory(destination);
                string destinationFile = destination + "\\" + Path.GetFileName(source);
                if (updater != null)
                    updater(destinationFile, EventType.Init);

                File.Copy(source, destinationFile, overwrite);

                if (updater != null)
                    updater(destinationFile, EventType.End);
            }
            else
            {
                if (Directory.Exists(source))
                {
                    DirectoryInfo sourceInfo = new DirectoryInfo(source);
                    string realDestination = destination + "\\" + sourceInfo.Name;
                    if (!Directory.Exists(realDestination))
                        Directory.CreateDirectory(realDestination);
                       
                    foreach (string directory in Directory.GetDirectories(source))
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(directory);
                        string dirDest = realDestination + "\\" + dirInfo.Name;
                        Copy(directory, realDestination, overwrite, updater);
                    }                    
                    foreach (string file in Directory.GetFiles(source))
                    {
                        Copy(file, realDestination, overwrite, updater);
                    }

                }
            }
        }

        public static void Delete(string elementPath)
        {
            if (File.Exists(elementPath))
            {
               File.Delete(elementPath);
            }
            else
            {
                if (Directory.Exists(elementPath))
                {
                   Directory.Delete(elementPath, true);
                }
            }
        }
    }
}
