using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace sharpCommander
{
    public partial class SearchDialog : Form
    {
        private string searchDirectory;
        private List<string> searchResults;

        public string SelectedFile { get; private set; }

        public SearchDialog(string directory)
        {
            InitializeComponent();
            searchDirectory = directory;
            lblDirectory.Text = "Buscar en: " + directory;
            searchResults = new List<string>();
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            string pattern = txtPattern.Text.Trim();

            if (string.IsNullOrEmpty(pattern))
            {
                MessageBox.Show("Ingrese un patrón de búsqueda", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            searchResults.Clear();
            listViewResults.Items.Clear();
            lblStatus.Text = "Buscando...";
            Application.DoEvents();

            try
            {
                SearchFiles(searchDirectory, pattern, chkRecursive.Checked);
                lblStatus.Text = "Encontrados: " + searchResults.Count + " archivo(s)";

                foreach (string file in searchResults)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    ListViewItem item = new ListViewItem(fileInfo.Name);
                    item.SubItems.Add(fileInfo.DirectoryName);
                    item.SubItems.Add(String.Format("{0} KB", fileInfo.Length / 1024));
                    item.SubItems.Add(String.Format("{0:g}", fileInfo.LastWriteTime));
                    item.Tag = file;
                    listViewResults.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error durante la búsqueda: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Error en la búsqueda";
            }
        }

        private void SearchFiles(string directory, string pattern, bool recursive)
        {
            try
            {
                // Buscar archivos en el directorio actual
                string[] files = Directory.GetFiles(directory, pattern);
                searchResults.AddRange(files);

                // Si es recursivo, buscar en subdirectorios
                if (recursive)
                {
                    string[] directories = Directory.GetDirectories(directory);
                    foreach (string dir in directories)
                    {
                        SearchFiles(dir, pattern, recursive);
                    }
                }
            }
            catch
            {
                // Ignorar directorios sin acceso
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void listViewResults_DoubleClick(object sender, EventArgs e)
        {
            if (listViewResults.SelectedItems.Count > 0)
            {
                SelectedFile = listViewResults.SelectedItems[0].Tag.ToString();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void txtPattern_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnSearch_Click(sender, e);
            }
        }
    }
}
