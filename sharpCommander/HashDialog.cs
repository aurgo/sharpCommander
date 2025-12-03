using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace sharpCommander
{
    public partial class HashDialog : Form
    {
        public HashDialog(string filePath)
        {
            InitializeComponent();
            CalculateAndDisplayHashes(filePath);
        }

        private void CalculateAndDisplayHashes(string filePath)
        {
            if (!File.Exists(filePath))
            {
                txtHashes.Text = "Error: El archivo no existe.";
                return;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("Archivo: " + fileInfo.Name);
                sb.AppendLine("Ruta: " + fileInfo.FullName);
                sb.AppendLine("Tamaño: " + FormatFileSize(fileInfo.Length));
                sb.AppendLine();
                sb.AppendLine("Calculando hashes...");
                sb.AppendLine();

                txtHashes.Text = sb.ToString();
                Application.DoEvents();

                using (FileStream stream = File.OpenRead(filePath))
                {
                    sb = new StringBuilder();
                    sb.AppendLine("Archivo: " + fileInfo.Name);
                    sb.AppendLine("Ruta: " + fileInfo.FullName);
                    sb.AppendLine("Tamaño: " + FormatFileSize(fileInfo.Length));
                    sb.AppendLine();

                    // MD5
                    stream.Position = 0;
                    using (MD5 md5 = MD5.Create())
                    {
                        byte[] hash = md5.ComputeHash(stream);
                        sb.AppendLine("MD5: " + BitConverter.ToString(hash).Replace("-", ""));
                    }

                    // SHA1
                    stream.Position = 0;
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        byte[] hash = sha1.ComputeHash(stream);
                        sb.AppendLine("SHA1: " + BitConverter.ToString(hash).Replace("-", ""));
                    }

                    // SHA256
                    stream.Position = 0;
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        byte[] hash = sha256.ComputeHash(stream);
                        sb.AppendLine("SHA256: " + BitConverter.ToString(hash).Replace("-", ""));
                    }
                }

                txtHashes.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                txtHashes.Text = "Error al calcular hashes: " + ex.Message;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return String.Format("{0:0.##} {1}", len, sizes[order]);
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtHashes.Text))
            {
                Clipboard.SetText(txtHashes.Text);
                MessageBox.Show("Hashes copiados al portapapeles", "Información", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
