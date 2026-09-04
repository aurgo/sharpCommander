using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace sharpCommander
{
    public partial class IUFileTabs : UserControl
    {
        FileExplorer fileExplorer = new FileExplorer();

        delegate void fileExplorer_OnFileSystemChangeCallBack(DirectoryInfo path, long numbersOfElements);

        DirectoryInfo directory;

        #region CONSTRUCTORS

        public IUFileTabs()
        {
            InitializeComponent();

            listView.Columns.Add("name", "Name", 200);
            listView.Columns.Add("size", "Size", 50);
            listView.Columns.Add("extension", "Extension", 65);
            listView.Columns.Add("date", "Date", 100);

            listView.LargeImageList = new ImageList();
            listView.SmallImageList = listView.LargeImageList;

            listView.LargeImageList.Images.Add(Properties.Resources.OpenFolder);
            listView.LargeImageList.Images.Add(Properties.Resources.Folder);
            listView.LargeImageList.Images.Add(Properties.Resources.File);
            listView.LargeImageList.Images.Add(Properties.Resources.Disk);
            listView.LargeImageList.Images.Add(Properties.Resources.Dvd);

            fileExplorer.OnFileSystemChange += new FileExplorer.OnFileSystemChangeHandler(fileExplorer_OnFileSystemChange);
            listView.RetrieveVirtualItem += new RetrieveVirtualItemEventHandler(listView_RetrieveVirtualItem);
        }

        #endregion

        private void execItem()
        {
            if (listView.SelectedIndices.Count == 0)
                return;

            if (directory == null)
            {
                SetDirectory(listView.Items[listView.SelectedIndices[0]].Text);
                return;
            }
            if (listView.SelectedIndices[0] == 0)
            {
                if (directory.Parent != null)
                    SetDirectory(directory.Parent.FullName);
                else
                    SetDirectory(null);
                return;
            }
            string execItem = directory.FullName + "\\" + listView.Items[listView.SelectedIndices[0]].Text;
            if (Directory.Exists(execItem))
                SetDirectory(execItem);
            else
            {
                if (File.Exists(execItem))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(execItem);
                    }
                    catch
                    {
                    }
                }
            }
        }

        #region GETS / SETS

        public void SetDirectory(string path)
        {
            if (directory != null && directory.FullName == path)
                return;

            if (Directory.Exists(path))
            {
                string cacheDirectory = null;
                if (directory != null)
                    cacheDirectory = directory.FullName;
                directory = new DirectoryInfo(path);
                try
                {
                    fileExplorer.setDirectory(directory);
                }
                catch (Exception e)
                {
                    directory = new DirectoryInfo(cacheDirectory);
                    fileExplorer.setDirectory(directory);
                    MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                tabControl.SelectedTab.Text = directory.Name;
                comboDir.Text = directory.FullName;

                string numDirs = fileExplorer.GetNumberOfDirectories().ToString();
                string numFiles = fileExplorer.GetNumberOfFiles().ToString();

                if (!comboDir.Items.Contains(path))
                    comboDir.Items.Add(path);

                tsStatus.Text = numDirs + " Directorios y " + numFiles + " Ficheros";
            }
            else
            {
                directory = null;
                fileExplorer.setDirectory(directory);
                tabControl.SelectedTab.Text = "Mi PC";
                comboDir.Text = "Mi PC";

                tsStatus.Text = fileExplorer.GetNumbersOfDrivers().ToString() + " Unidades de disco";
            }
            listView.VirtualListSize = fileExplorer.GetNumbersOfElements() + 1;
            listView.Invalidate();
        }

        public string GetDirectory()
        {
            return directory.FullName;
        }

        public List<FileSystemInfo> GetSelectedItems()
        {
            List<FileSystemInfo> selectedList = new List<FileSystemInfo>();

            foreach (int itemIndex in listView.SelectedIndices)
            {
                FileExplorer.DrvDirFileInfo drvDirFileInfo = fileExplorer.GetFileSystemInfo(itemIndex - 1);
                if (drvDirFileInfo.isDriver)
                {
                    selectedList.Clear();
                    return selectedList;
                }
                else
                {
                    selectedList.Add(drvDirFileInfo.fileSystemInfo);
                }
            }
            return selectedList;
        }

        public void SetStatusBar(string status)
        {
            tsStatus.Text = status;
        }

        public string GetSelectedFile()
        {
            if (listView.SelectedIndices.Count == 0 || listView.SelectedIndices[0] == 0)
                return null;

            if (directory == null)
                return null;

            FileExplorer.DrvDirFileInfo drvDirFileInfo = fileExplorer.GetFileSystemInfo(listView.SelectedIndices[0] - 1);
            if (drvDirFileInfo.fileSystemInfo != null && File.Exists(drvDirFileInfo.fileSystemInfo.FullName))
            {
                return drvDirFileInfo.fileSystemInfo.FullName;
            }
            return null;
        }

        public FileSystemInfo GetSelectedItem()
        {
            if (listView.SelectedIndices.Count == 0 || listView.SelectedIndices[0] == 0)
                return null;

            if (directory == null)
                return null;

            FileExplorer.DrvDirFileInfo drvDirFileInfo = fileExplorer.GetFileSystemInfo(listView.SelectedIndices[0] - 1);
            return drvDirFileInfo.fileSystemInfo;
        }

        public void RefreshView()
        {
            if (directory != null)
            {
                SetDirectory(directory.FullName);
            }
        }

        #endregion

        #region EVENTS

        void fileExplorer_OnFileSystemChange(DirectoryInfo path, long numbersOfElements)
        {
            if (this.InvokeRequired)
            {
                fileExplorer_OnFileSystemChangeCallBack cbf = new fileExplorer_OnFileSystemChangeCallBack(fileExplorer_OnFileSystemChange);
                this.Invoke(cbf, new object[] { path, numbersOfElements });
            }
            else
            {
                tabControl.SelectedTab.Text = path.Name;

                listView.VirtualListSize = Convert.ToInt32(numbersOfElements) + 1;
            }
        }

        void listView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (directory != null)
            {
                if (e.ItemIndex == 0)
                {
                    e.Item = new ListViewItem("..");
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.ImageIndex = 0;
                    return;
                }
                FileExplorer.DrvDirFileInfo drvDirFileInfo = fileExplorer.GetFileSystemInfo(e.ItemIndex - 1);

                ListViewItem item = new ListViewItem();

                if (drvDirFileInfo.fileSystemInfo == null)
                {
                    e.Item = new ListViewItem("");
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.ImageIndex = 0;
                    return;
                }


                item.Text = drvDirFileInfo.fileSystemInfo.Name;

                if (File.Exists(drvDirFileInfo.fileSystemInfo.FullName))
                {
                    FileInfo fileInfo = new FileInfo(drvDirFileInfo.fileSystemInfo.FullName);
                    item.SubItems.Add(String.Format("{0} Kb", fileInfo.Length / 1024));

                    if (drvDirFileInfo.fileSystemInfo.Extension.ToUpper() == ".EXE")
                    {
                        listView.LargeImageList.Images.Add(Icon.ExtractAssociatedIcon(drvDirFileInfo.fileSystemInfo.FullName).ToBitmap());
                        item.ImageIndex = listView.LargeImageList.Images.Count - 1;
                    }
                    else
                    {
                        item.ImageIndex = 2;
                    }
                }
                else
                {
                    item.ImageIndex = 1;
                    item.SubItems.Add("<DIR>");
                }
                item.SubItems.Add(drvDirFileInfo.fileSystemInfo.Extension);
                item.SubItems.Add(String.Format("{0:g}", drvDirFileInfo.fileSystemInfo.LastWriteTime));

                e.Item = item;
            }
            else
            {
                FileExplorer.DrvDirFileInfo drvDirFileInfo = fileExplorer.GetFileSystemInfo(e.ItemIndex);

                e.Item = new ListViewItem(drvDirFileInfo.driverInfo.Name);
                if (drvDirFileInfo.driverInfo.IsReady)
                {
                    e.Item.SubItems.Add(String.Format("{0}", drvDirFileInfo.driverInfo.TotalSize / (1024 * 1024)));
                    e.Item.SubItems.Add(drvDirFileInfo.driverInfo.VolumeLabel);
                    e.Item.SubItems.Add(drvDirFileInfo.driverInfo.DriveFormat.ToString());
                    e.Item.ImageIndex = 3;
                }
                else
                {
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.SubItems.Add("");
                    e.Item.ImageIndex = 4;
                }
            }
        }

        private void listView_DoubleClick(object sender, EventArgs e)
        {
            execItem();
        }



        private void tsBtnDrives_Click(object sender, EventArgs e)
        {
            SetDirectory(null);
        }

        private void comboDir_SelectedValueChanged(object sender, EventArgs e)
        {
            if (comboDir.SelectedItem != null && Directory.Exists(comboDir.SelectedItem.ToString()))
                SetDirectory(comboDir.SelectedItem.ToString());
        }

        private void comboDir_TextChanged(object sender, EventArgs e)
        {
            if (Directory.Exists(comboDir.Text))
            {
                SetDirectory(comboDir.Text);
                comboDir.SelectionStart = comboDir.Text.Length;
                comboDir.SelectionLength = 0;
            }
        }

        private void listView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Enter)
            {
                execItem();
            }
            base.OnKeyDown(e);
        }

        private void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {

            tabControl.SelectedTab.Controls.Add(listView);
            SetDirectory(directory.FullName);
        }

        #endregion

        private void tabControl_Selecting(object sender, TabControlCancelEventArgs e)
        {

        }
    }
}
