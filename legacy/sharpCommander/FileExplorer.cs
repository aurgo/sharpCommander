using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;

namespace sharpCommander
{
   class FileExplorer
   {
      DirectoryInfo directory;

      List<FileSystemInfo> fileSystemInfoList;

      FileSystemWatcher fileSystemWatcher;

      public struct DrvDirFileInfo
      {
         public bool isDriver;
         public FileSystemInfo fileSystemInfo;
         public DriveInfo driverInfo;
      }

      public delegate void OnFileSystemChangeHandler(DirectoryInfo path, long numbersOfElements);
      public event OnFileSystemChangeHandler OnFileSystemChange;      

      #region CONSTRUCTORS

      public FileExplorer()
      {
         fileSystemWatcher = new FileSystemWatcher();
         fileSystemWatcher.Created += new FileSystemEventHandler(fileSystemWatcher_Event);
         fileSystemWatcher.Changed += new FileSystemEventHandler(fileSystemWatcher_Event);
         fileSystemWatcher.Renamed += new RenamedEventHandler(fileSystemWatcher_Renamed);
         fileSystemWatcher.Deleted += new FileSystemEventHandler(fileSystemWatcher_Event);
         fileSystemWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
         fileSystemWatcher.Filter = "*.*";                  
      }
      
      #endregion

      #region SETS / GETS

      public void setDirectory(DirectoryInfo path)
      {
         directory = path;
         RefreshFileSystemInfo();
         if (path != null)
         {
            
            fileSystemWatcher.IncludeSubdirectories = false;
            fileSystemWatcher.Path = directory.FullName;
            fileSystemWatcher.EnableRaisingEvents = true;
         }
         else
         {
            fileSystemWatcher.EnableRaisingEvents = false;
         }
      }

      public string getDirectory()
      {         
         return directory.FullName;
      }

      public int GetNumbersOfElements()
      {
         if (fileSystemInfoList == null)
            return DriveInfo.GetDrives().Length - 1;
         else
            return fileSystemInfoList.Count;
      }

      public int GetNumberOfDirectories()
      {
         return directory.GetDirectories().Length;
      }

      public int GetNumberOfFiles()
      {
         return directory.GetFiles().Length;
      }

      public int GetNumbersOfDrivers()
      {
         return DriveInfo.GetDrives().Length;
      }

      public void RefreshFileSystemInfo()
      {
         fileSystemInfoList = new List<FileSystemInfo>();

         if (directory != null)
         {
            fileSystemInfoList.AddRange(directory.GetDirectories() as FileSystemInfo[]);
            fileSystemInfoList.AddRange(directory.GetFiles() as FileSystemInfo[]);
         }
         else
         {
            fileSystemInfoList = null;
         }
         return;
      }

      public DrvDirFileInfo GetFileSystemInfo(int index)
      {
         DrvDirFileInfo drvDirFile = new DrvDirFileInfo();
         try
         {
            if (fileSystemInfoList != null)
            {
               drvDirFile.isDriver = false;
               drvDirFile.fileSystemInfo = fileSystemInfoList[index];
            }
            else
            {
               drvDirFile.isDriver = true;
               drvDirFile.driverInfo = DriveInfo.GetDrives()[index];
            }
         }
         catch
         {
         }
         return drvDirFile;
      }

      #endregion

      #region EVENTS

      void fileSystemWatcher_Renamed(object sender, RenamedEventArgs e)
      {
         RefreshFileSystemInfo();
         if (OnFileSystemChange != null)
            OnFileSystemChange(directory, fileSystemInfoList.Count);         
      }

      void fileSystemWatcher_Event(object sender, FileSystemEventArgs e)
      {
         if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath) && !File.Exists(e.FullPath))
            return;

         RefreshFileSystemInfo();
         if (OnFileSystemChange != null)
            OnFileSystemChange(directory, fileSystemInfoList.Count);
      }

      #endregion
   }

}
