using System;
using System.IO;
using System.Windows.Forms;

namespace sharpCommander
{
    public partial class RenameDialog : Form
    {
        private string originalPath;
        private bool isDirectory;

        public string NewName { get; private set; }

        public RenameDialog(string path)
        {
            InitializeComponent();

            originalPath = path;
            isDirectory = Directory.Exists(path);

            if (isDirectory)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                lblCurrentName.Text = "Nombre actual: " + dirInfo.Name;
                txtNewName.Text = dirInfo.Name;
                this.Text = "Renombrar Carpeta";
            }
            else
            {
                FileInfo fileInfo = new FileInfo(path);
                lblCurrentName.Text = "Nombre actual: " + fileInfo.Name;
                txtNewName.Text = fileInfo.Name;
                this.Text = "Renombrar Archivo";
            }

            txtNewName.SelectAll();
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string newName = txtNewName.Text.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("El nombre no puede estar vacío", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validar caracteres inválidos
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                if (newName.Contains(c.ToString()))
                {
                    MessageBox.Show("El nombre contiene caracteres inválidos", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            // Verificar si el nombre es el mismo
            string currentName = isDirectory ? new DirectoryInfo(originalPath).Name : new FileInfo(originalPath).Name;
            if (newName == currentName)
            {
                MessageBox.Show("El nuevo nombre es igual al actual", "Información", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            NewName = newName;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void txtNewName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnOk_Click(sender, e);
            }
        }
    }
}
