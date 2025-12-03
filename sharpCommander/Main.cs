using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace sharpCommander
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();

            iuFileTabs1.SetDirectory(@"C:\Temp");
            iuFileTabs2.SetDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        private void Copy()
        {
            List<FileSystemInfo> sourceList = new List<FileSystemInfo>();
            string destinationFolder = string.Empty;

            if (iuFileTabs1.ContainsFocus)
            {
                sourceList = iuFileTabs1.GetSelectedItems();
                destinationFolder = iuFileTabs2.GetDirectory();
            }
            if (iuFileTabs2.ContainsFocus)
            {
                sourceList = iuFileTabs2.GetSelectedItems();
                destinationFolder = iuFileTabs1.GetDirectory();
            }
            if (sourceList.Count == 0 || !Directory.Exists(destinationFolder))
                return;

            foreach (FileSystemInfo fileSystemInfo in sourceList)
            {
                object[] parameters = new object[] { fileSystemInfo.FullName, destinationFolder, true, null };
                Thread thread = new Thread(CopyInThread);
                thread.Start(parameters as object);
            }
        }

        void CopyInThread(object parameters)
        {
            object[] aParameters = parameters as object[];
            string source = Convert.ToString(aParameters[0]);
            string destination = Convert.ToString(aParameters[1]);
            bool overwrite = Convert.ToBoolean(aParameters[2]);
            FileUtils.updaterCallBack updater = aParameters[2] as FileUtils.updaterCallBack;
            try
            {
                FileUtils.Copy(source, destination, overwrite, CopyUpdaterCallBack);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CopyUpdaterCallBack("", FileUtils.EventType.End);
            }
        }

        private void Delete()
        {
            List<FileSystemInfo> deleteList = new List<FileSystemInfo>();

            if (iuFileTabs1.ContainsFocus)
            {
                deleteList = iuFileTabs1.GetSelectedItems();
            }
            if (iuFileTabs2.ContainsFocus)
            {
                deleteList = iuFileTabs2.GetSelectedItems();
            }
            if (deleteList.Count == 0)
                return;

            foreach (FileSystemInfo fileSystemInfo in deleteList)
            {
                switch (MessageBox.Show(String.Format("¿Esta seguro de que desea eliminar {0}?", fileSystemInfo.Name), "ELIMINAR", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        try
                        {
                            tsProgressBar.Visible = true;
                            tsLblStatus.Text = "Eliminando : " + fileSystemInfo.FullName;
                            FileUtils.Delete(fileSystemInfo.FullName);
                            tsLblStatus.Text = "";
                            tsProgressBar.Visible = false;
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        break;
                    case DialogResult.Cancel:
                        return;
                }
            }
        }

        #region EVENTS

        void CopyUpdaterCallBack(string file, FileUtils.EventType eventType)
        {
            if (this.InvokeRequired)
            {
                FileUtils.updaterCallBack cbf = new FileUtils.updaterCallBack(CopyUpdaterCallBack);
                this.Invoke(cbf, new object[] { file, eventType });
            }
            else
            {
                tsLblStatus.Text = "Copiando: " + file;
                tsProgressBar.Visible = true;
                if (eventType == FileUtils.EventType.End)
                {
                    tsLblStatus.Text = "";
                    tsProgressBar.Visible = false;
                }
            }
        }

        private void acercaDeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox aboutBox = new AboutBox();
            aboutBox.ShowDialog(this);
        }

        private void salirToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void copiarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Copy();
        }

        private void borrarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Delete();
        }

        private void verHashesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string selectedFile = null;
            if (iuFileTabs1.ContainsFocus)
            {
                selectedFile = iuFileTabs1.GetSelectedFile();
            }
            else if (iuFileTabs2.ContainsFocus)
            {
                selectedFile = iuFileTabs2.GetSelectedFile();
            }

            if (selectedFile == null)
            {
                MessageBox.Show("Seleccione un archivo para ver sus hashes", "Información", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            HashDialog hashDialog = new HashDialog(selectedFile);
            hashDialog.ShowDialog(this);
        }

        private void renombrarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileSystemInfo selectedItem = null;
            IUFileTabs activeTab = null;

            if (iuFileTabs1.ContainsFocus)
            {
                selectedItem = iuFileTabs1.GetSelectedItem();
                activeTab = iuFileTabs1;
            }
            else if (iuFileTabs2.ContainsFocus)
            {
                selectedItem = iuFileTabs2.GetSelectedItem();
                activeTab = iuFileTabs2;
            }

            if (selectedItem == null)
            {
                MessageBox.Show("Seleccione un archivo o carpeta para renombrar", "Información", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RenameDialog renameDialog = new RenameDialog(selectedItem.FullName);
            if (renameDialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    string directory = Path.GetDirectoryName(selectedItem.FullName);
                    string newPath = Path.Combine(directory, renameDialog.NewName);

                    if (selectedItem is DirectoryInfo)
                    {
                        Directory.Move(selectedItem.FullName, newPath);
                    }
                    else
                    {
                        File.Move(selectedItem.FullName, newPath);
                    }

                    activeTab.RefreshView();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al renombrar: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void buscarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string searchDirectory = null;
            IUFileTabs activeTab = null;

            if (iuFileTabs1.ContainsFocus)
            {
                searchDirectory = iuFileTabs1.GetDirectory();
                activeTab = iuFileTabs1;
            }
            else if (iuFileTabs2.ContainsFocus)
            {
                searchDirectory = iuFileTabs2.GetDirectory();
                activeTab = iuFileTabs2;
            }

            if (searchDirectory == null)
            {
                MessageBox.Show("Navegue a un directorio para buscar archivos", "Información", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SearchDialog searchDialog = new SearchDialog(searchDirectory);
            if (searchDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(searchDialog.SelectedFile))
            {
                string directory = Path.GetDirectoryName(searchDialog.SelectedFile);
                activeTab.SetDirectory(directory);
            }
        }

        #endregion

        private void Main_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyData)
            {
                case Keys.F5:
                    Copy();
                    break;
                case Keys.Delete:
                    Delete();
                    break;
                case Keys.F2:
                    renombrarToolStripMenuItem_Click(sender, e);
                    break;
                case Keys.Control | Keys.H:
                    verHashesToolStripMenuItem_Click(sender, e);
                    break;
                case Keys.Control | Keys.F:
                    buscarToolStripMenuItem_Click(sender, e);
                    break;
            }
        }
    }
}
