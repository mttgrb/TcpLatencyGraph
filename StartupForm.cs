using System;
using System.Drawing;
using System.Windows.Forms;

namespace TcpLatencyGraph
{
    public partial class StartupForm : Form
    {
        private readonly Options _options;

        internal StartupForm(Options options)
        {
            _options = options ?? new Options();

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
            string portText = txtPort.Text.Trim();
            if (target.Length == 0)
            {
                ShowError("Please enter a hostname or IP address.");
                return;
            }

            string host;
            int port;
            // "host:port" typed into the host box works too
            if (portText.Length > 0 || !Target.TryParseHostPort(target, out host, out port))
            {
                host = Target.CleanHost(target);
                if (!Target.IsValidHost(host))
                {
                    ShowError("Invalid hostname or IP address.");
                    return;
                }

                if (!Target.TryParsePort(portText, out port))
                {
                    ShowError("Invalid port number.");
                    return;
                }
            }

            // Everything OK → open graph
            GraphForm graph = new GraphForm(host, port, _options);
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
