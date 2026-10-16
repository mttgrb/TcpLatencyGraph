namespace TcpLatencyGraph
{
    partial class GraphForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Button btnStartStop;
        private System.Windows.Forms.Button btnDownload;

        private void InitializeComponent()
        {
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.topPanel = new System.Windows.Forms.Panel();
            this.btnStartStop = new System.Windows.Forms.Button();
            this.btnDownload = new System.Windows.Forms.Button();

            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.topPanel.SuspendLayout();
            this.SuspendLayout();

            // btnStartStop
            this.btnStartStop.Location = new System.Drawing.Point(8, 6);
            this.btnStartStop.Size = new System.Drawing.Size(80, 26);
            this.btnStartStop.Name = "btnStartStop";
            this.btnStartStop.Text = "Stop";
            this.btnStartStop.UseVisualStyleBackColor = true;
            this.btnStartStop.Click += new System.EventHandler(this.BtnStartStop_Click);

            // btnDownload
            this.btnDownload.Location = new System.Drawing.Point(94, 6);
            this.btnDownload.Size = new System.Drawing.Size(80, 26);
            this.btnDownload.Name = "btnDownload";
            this.btnDownload.Text = "Download";
            this.btnDownload.UseVisualStyleBackColor = true;
            this.btnDownload.Click += new System.EventHandler(this.BtnDownload_Click);

            // topPanel
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topPanel.Height = 38;
            this.topPanel.Name = "topPanel";
            this.topPanel.Controls.Add(this.btnStartStop);
            this.topPanel.Controls.Add(this.btnDownload);

            // pictureBox1
            this.pictureBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Paint += new System.Windows.Forms.PaintEventHandler(this.PictureBox1_Paint);

            // GraphForm
            this.ClientSize = new System.Drawing.Size(1200, 488);
            this.MinimumSize = new System.Drawing.Size(640, 360);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.topPanel);
            this.Text = "TCP Latency Graph";

            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.topPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}
