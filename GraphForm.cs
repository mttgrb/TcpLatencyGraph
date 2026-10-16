using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace TcpLatencyGraph
{
    public partial class GraphForm : Form
    {
        private string targetIP;
        private int targetPort;

        private List<int> latency = new List<int>();
        private List<DateTime> timestamps = new List<DateTime>();
        private int timeoutCount = 0;

        private System.Windows.Forms.Timer timer;
        private bool isTestingInProgress = false;

        // Hover functionality
        private Point mousePosition = Point.Empty;
        private bool isHovering = false;
        private int hoveredDataIndex = -1;

        private const int TIMEOUT_THRESHOLD = 1000; // Max ms stored
        private const int CONNECTION_TIMEOUT_MS = 3000; // 3 second max timeout

        public GraphForm(string ip, int port)
        {
            targetIP = ip;
            targetPort = port;

            InitializeComponent();
            this.Icon = new Icon(typeof(GraphForm), "Resources.icon.ico");
            this.Text = $"TCP Latency Graph – {ip}:{port}";

            // Enable double buffering for smoother rendering
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.UserPaint | 
                          ControlStyles.DoubleBuffer, true);

            // Set up mouse events for hover functionality
            pictureBox1.MouseMove += pictureBox1_MouseMove;
            pictureBox1.MouseLeave += pictureBox1_MouseLeave;

            timer = new System.Windows.Forms.Timer
            {
                Interval = 1000
            };

            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            mousePosition = e.Location;
            isHovering = true;
            
            // Find the closest data point to the mouse X position
            hoveredDataIndex = GetClosestDataPoint(e.X);
            
            // Redraw to show the hover dot and tooltip
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseLeave(object sender, EventArgs e)
        {
            isHovering = false;
            hoveredDataIndex = -1;
            pictureBox1.Invalidate();
        }

        private int GetClosestDataPoint(int mouseX)
        {
            if (latency.Count <= 1) return -1;

            int left = 60;
            int width = pictureBox1.Width - left;
            float totalSeconds = Math.Max(300, latency.Count);

            // Convert mouse X to data index
            float relativeX = (mouseX - left) / (float)width;
            int dataIndex = (int)(relativeX * totalSeconds);

            // Clamp to valid range
            if (dataIndex < 0) return -1;
            if (dataIndex >= latency.Count) return latency.Count - 1;

            return dataIndex;
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            // Skip if previous test is still running
            if (isTestingInProgress)
                return;

            isTestingInProgress = true;

            try
            {
                int result = await TestTcpLatencyAsync();

                if (result < 0 || result >= TIMEOUT_THRESHOLD)
                {
                    timeoutCount++;
                    latency.Add(TIMEOUT_THRESHOLD);
                }
                else
                {
                    latency.Add(result);
                }

                // Store timestamp for this measurement
                timestamps.Add(DateTime.Now);

                // Only invalidate if form is still visible
                if (this.Visible && !this.IsDisposed)
                {
                    pictureBox1.Invalidate();
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected errors and treat as timeout
                Debug.WriteLine($"TCP test error: {ex.Message}");
                timeoutCount++;
                latency.Add(TIMEOUT_THRESHOLD);
                timestamps.Add(DateTime.Now);
                
                if (this.Visible && !this.IsDisposed)
                {
                    pictureBox1.Invalidate();
                }
            }
            finally
            {
                isTestingInProgress = false;
            }
        }

        private async Task<int> TestTcpLatencyAsync()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var stopwatch = Stopwatch.StartNew();

                    // Set timeouts before connection
                    client.ReceiveTimeout = CONNECTION_TIMEOUT_MS;
                    client.SendTimeout = CONNECTION_TIMEOUT_MS;

                    // Create cancellation token for timeout
                    using (var cts = new CancellationTokenSource(CONNECTION_TIMEOUT_MS))
                    {
                        // Run connection on background thread with timeout
                        var connectTask = Task.Run(() => 
                        {
                            try
                            {
                                client.Connect(targetIP, targetPort);
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        }, cts.Token);

                        // Wait for connection or timeout
                        bool connected = await connectTask;
                        stopwatch.Stop();

                        if (connected && client.Connected)
                        {
                            return (int)stopwatch.ElapsedMilliseconds;
                        }
                        else
                        {
                            return -1; // Connection failed
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred
                return CONNECTION_TIMEOUT_MS;
            }
            catch (Exception ex)
            {
                // Any other error (network, DNS, etc.)
                Debug.WriteLine($"TCP connection error: {ex.Message}");
                return -1;
            }
        }

        private int GetDynamicYMax()
        {
            if (latency.Count == 0)
                return 50;

            var validValues = latency.Where(v => v < TIMEOUT_THRESHOLD);

            if (!validValues.Any())
                return 50;

            int max = validValues.Max();
            return Math.Max(max, 50);
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            
            // Enable anti-aliasing for smoother lines
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            g.Clear(Color.FromArgb(230, 255, 230));

            int left = 60;
            int bottom = 30;
            int width = pictureBox1.Width - left;
            int height = pictureBox1.Height - bottom;

            if (width <= 0 || height <= 0)
                return;

            int YMax = GetDynamicYMax();
            float totalSeconds = Math.Max(300, latency.Count);

            // ----- GRID + Y LABELS -----
            using (var gridPen = new Pen(Color.LightGray, 1))
            using (var textBrush = new SolidBrush(Color.Black))
            {
                for (int i = 0; i <= 10; i++)
                {
                    float y = height * i / 10f;
                    g.DrawLine(gridPen, left, y, pictureBox1.Width, y);

                    int label = (int)(YMax * (1 - i / 10f));
                    g.DrawString(label.ToString(), DefaultFont, textBrush, 5, y - 7);
                }
            }

            // ----- LATENCY LINE -----
            if (latency.Count > 1)
            {
                using (var linePen = new Pen(Color.Black, 2))
                {
                    for (int i = 1; i < latency.Count; i++)
                    {
                        float x1 = left + (i - 1) / totalSeconds * width;
                        float x2 = left + i / totalSeconds * width;

                        int v1 = latency[i - 1];
                        int v2 = latency[i];

                        float y1 = height - Math.Min(v1, YMax) / (float)YMax * height;
                        float y2 = height - Math.Min(v2, YMax) / (float)YMax * height;

                        g.DrawLine(linePen, x1, y1, x2, y2);
                    }
                }
            }

            // ----- HOVER DOT AND TOOLTIP -----
            if (isHovering && hoveredDataIndex >= 0 && hoveredDataIndex < latency.Count && hoveredDataIndex < timestamps.Count)
            {
                // Calculate dot position
                float dotX = left + hoveredDataIndex / totalSeconds * width;
                int latencyValue = latency[hoveredDataIndex];
                float dotY = height - Math.Min(latencyValue, YMax) / (float)YMax * height;

                // Draw hover dot
                using (var dotBrush = new SolidBrush(Color.Red))
                using (var dotOutlinePen = new Pen(Color.White, 2))
                {
                    float dotRadius = 4;
                    g.FillEllipse(dotBrush, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
                    g.DrawEllipse(dotOutlinePen, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
                }

                // Draw vertical line from dot to bottom
                using (var hoverLinePen = new Pen(Color.Red, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    g.DrawLine(hoverLinePen, dotX, dotY, dotX, height);
                }

                // Draw tooltip with date and time
                DateTime timestamp = timestamps[hoveredDataIndex];
                string tooltipText = $"Date: {timestamp:yyyy-MM-dd}\nTime: {timestamp:HH:mm:ss}\nLatency: {(latencyValue >= TIMEOUT_THRESHOLD ? "Timeout" : latencyValue + " ms")}\nSample: {hoveredDataIndex + 1}";

                using (var tooltipFont = new Font(DefaultFont.FontFamily, 8))
                using (var tooltipBrush = new SolidBrush(Color.Black))
                using (var tooltipBackground = new SolidBrush(Color.FromArgb(240, 255, 255, 192)))
                using (var tooltipBorder = new Pen(Color.Black, 1))
                {
                    SizeF textSize = g.MeasureString(tooltipText, tooltipFont);
                    float tooltipX = dotX + 10;
                    float tooltipY = dotY - textSize.Height - 10;

                    // Adjust tooltip position if it goes off screen
                    if (tooltipX + textSize.Width > pictureBox1.Width - 10)
                        tooltipX = dotX - textSize.Width - 10;
                    if (tooltipY < 10)
                        tooltipY = dotY + 10;

                    RectangleF tooltipRect = new RectangleF(tooltipX - 5, tooltipY - 5, textSize.Width + 10, textSize.Height + 10);
                    
                    g.FillRectangle(tooltipBackground, tooltipRect);
                    g.DrawRectangle(tooltipBorder, Rectangle.Round(tooltipRect));
                    g.DrawString(tooltipText, tooltipFont, tooltipBrush, tooltipX, tooltipY);
                }
            }

            // ----- AXIS LABELS -----
            using (var textBrush = new SolidBrush(Color.Black))
            {
                g.DrawString("Latency (ms)", DefaultFont, textBrush, 10, 10);
                g.DrawString("Time (seconds)", DefaultFont, textBrush,
                    pictureBox1.Width / 2 - 50, pictureBox1.Height - 20);

                // Time counter bottom right
                g.DrawString(
                    totalSeconds.ToString("0") + " s",
                    DefaultFont,
                    textBrush,
                    left + width - 40,
                    pictureBox1.Height - 20
                );
            }

            DrawStats(g, left, width);
        }

        private void DrawStats(Graphics g, int left, int width)
        {
            var validValues = latency.Where(v => v < TIMEOUT_THRESHOLD).ToList();

            double avg = validValues.Any() ? validValues.Average() : 0;
            int min = validValues.Any() ? validValues.Min() : 0;
            int max = validValues.Any() ? validValues.Max() : 0;
            double loss = latency.Count > 0 ? (timeoutCount * 100.0 / latency.Count) : 0;

            string[] lines =
            {
                $"Samples: {latency.Count}",
                $"Min: {min} ms",
                $"Max: {max} ms",
                $"Avg: {avg:F1} ms",
                $"Timeouts: {timeoutCount}",
                $"Loss: {loss:F1}%"
            };

            int boxWidth = 160;
            int boxHeight = lines.Length * 18 + 10;
            int x = left + width - boxWidth - 10;

            Rectangle rect = new Rectangle(x, 10, boxWidth, boxHeight);

            using (var whiteBrush = new SolidBrush(Color.White))
            using (var blackPen = new Pen(Color.Black))
            using (var textBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(whiteBrush, rect);
                g.DrawRectangle(blackPen, rect);

                for (int i = 0; i < lines.Length; i++)
                {
                    g.DrawString(lines[i], DefaultFont, textBrush, x + 5, 15 + i * 18);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Stop timer and clean up
            timer?.Stop();
            timer?.Dispose();

            base.OnFormClosed(e);

            if (Application.OpenForms.Count == 0)
                Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer?.Stop();
                timer?.Dispose();
                
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}