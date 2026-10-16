using System;
using System.Drawing;
using System.Windows.Forms;

namespace TcpLatencyGraph
{
    public partial class StartupForm : Form
    {
        public StartupForm()
        {
            InitializeComponent();
            this.Icon = new Icon(typeof(StartupForm), "Resources.icon.ico");
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.AcceptButton = btnStart;

            // Auto-hide error when typing
            txtIP.TextChanged += (s, e) => lblError.Visible = false;
            txtPort.TextChanged += (s, e) => lblError.Visible = false;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            lblError.Visible = false;

            string target = txtIP.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                ShowError("Please enter a hostname or IP address.");
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
            {
                ShowError("Invalid port number.");
                return;
            }

            // Everything OK → open graph
            GraphForm graph = new GraphForm(target, port);
            graph.Show();
        }

        private void ShowError(string message)
        {
            lblError.Text = message;
            lblError.Visible = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            if (Application.OpenForms.Count == 0)
                Application.Exit();
        }
    }
}
