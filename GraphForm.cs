using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpLatencyGraph
{
    public partial class GraphForm : Form
    {
        private const int MinYMax = 50;               // ms, Y axis never goes below this
        private const int MinAxisSeconds = 300;       // X axis starts at 5 minutes and grows from there
        private const int MaxSamplesKept = 1000000;   // for the CSV export, the graph only shows the window
        private const int DnsTimeoutMs = 5000;
        private static readonly TimeSpan ReResolveInterval = TimeSpan.FromMinutes(5);

        // Layout in pixels
        private const int PlotLeft = 60;
        private const int PlotTop = 25;
        private const int PlotBottomMargin = 40;
        private const int StatsColumnWidth = 190;
        private const int MarkerOffset = 6;           // failure markers sit this far below the top edge

        private static readonly Color BackgroundColor = Color.FromArgb(230, 255, 230);
        private static readonly Color TimeoutColor = Color.Red;
        private static readonly Color RefusedColor = Color.DarkOrange;
        private static readonly double[] TimeSteps =
            { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800 };

        private readonly string _host;
        private readonly int _port;
        private readonly Options _options;
        private readonly bool _isLiteral;
        private IPAddress _address;
        private DateTime _lastResolved;

        private readonly List<Sample> _samples = new List<Sample>();
        private readonly Stopwatch _clock = new Stopwatch();
        private double _viewEnd;                      // right edge of the X axis, in seconds since start
        private int _sent, _timeouts, _refused, _errors;

        private readonly System.Windows.Forms.Timer _timer;
        private bool _running;
        private bool _busy;
        private int _runId;                           // bumped on start/stop/close so stale results get dropped
        private bool _closed;

        private SharedLog _log;
        private string _logError;

        private int _hoverIndex = -1;
        private Point _mouse;
        private bool _mouseInside;
        private readonly Font _tooltipFont;

        internal GraphForm(string host, int port, Options options)
        {
            _host = host;
            _port = port;
            _options = options ?? new Options();

            IPAddress literal;
            if (IPAddress.TryParse(host, out literal))
            {
                _isLiteral = true;
                _address = literal;
            }

            InitializeComponent();
            this.Icon = new Icon(typeof(GraphForm), "Resources.icon.ico");
            _tooltipFont = new Font(Font.FontFamily, 8);
            UpdateTitle();

            pictureBox1.MouseMove += PictureBox1_MouseMove;
            pictureBox1.MouseLeave += PictureBox1_MouseLeave;
            pictureBox1.Resize += (s, e) => RefreshGraph();

            OpenLog();

            _timer = new System.Windows.Forms.Timer { Interval = _options.IntervalMs };
            _timer.Tick += Timer_Tick;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartMeasuring();
        }

        // ----- MEASURING -----

        private void StartMeasuring()
        {
            _runId++;
            _running = true;
            _clock.Start();
            _timer.Start();
            btnStartStop.Text = "Stop";
            UpdateTitle();
            Timer_Tick(this, EventArgs.Empty);
        }

        // Pauses; Start continues on the same graph, the pause shows up as a gap
        private void StopMeasuring()
        {
            _runId++;
            _running = false;
            _timer.Stop();
            _viewEnd = Math.Max(_viewEnd, _clock.Elapsed.TotalSeconds);
            btnStartStop.Text = "Start";
            UpdateTitle();
            RefreshGraph();
        }

        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (_running)
                StopMeasuring();
            else
                StartMeasuring();
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            _viewEnd = Math.Max(_viewEnd, _clock.Elapsed.TotalSeconds);

            // Skip if previous test is still running, just keep the time axis moving
            if (_busy)
            {
                RefreshGraph();
                return;
            }

            _busy = true;
            int runId = _runId;
            Sample sample;
            try
            {
                sample = await MeasureAsync();
            }
            catch (Exception ex)
            {
                sample = new Sample(DateTime.Now, _clock.Elapsed.TotalSeconds, SampleStatus.Error, 0,
                                    _address?.ToString(), ex.Message);
            }
            finally
            {
                _busy = false;
            }

            if (_closed || runId != _runId)
                return;

            AddSample(sample);
        }

        private async Task<Sample> MeasureAsync()
        {
            DateTime time = DateTime.Now;
            double seconds = _clock.Elapsed.TotalSeconds;

            // DNS happens before the stopwatch starts, so it never counts as latency
            string dnsError = await EnsureResolvedAsync();
            IPAddress address = _address;
            if (address == null)
                return new Sample(time, seconds, SampleStatus.DnsFailed, 0, null, dnsError);

            string ip = address.ToString();
            time = DateTime.Now;
            seconds = _clock.Elapsed.TotalSeconds;

            using (var client = new TcpClient(address.AddressFamily))
            {
                var stopwatch = Stopwatch.StartNew();
                Task connect = client.ConnectAsync(address, _port);
                Task finished = await Task.WhenAny(connect, Task.Delay(_options.TimeoutMs));
                double ms = stopwatch.Elapsed.TotalMilliseconds;

                if (finished != connect)
                {
                    // Disposing the client aborts the pending connect
                    IgnoreFault(connect);
                    return new Sample(time, seconds, SampleStatus.Timeout, ms, ip, null);
                }

                if (connect.IsFaulted || connect.IsCanceled)
                {
                    Exception error = connect.Exception?.GetBaseException();
                    var socketError = error as SocketException;
                    if (socketError != null && socketError.SocketErrorCode == SocketError.ConnectionRefused)
                        return new Sample(time, seconds, SampleStatus.Refused, ms, ip, null);
                    if (socketError != null && socketError.SocketErrorCode == SocketError.TimedOut)
                        return new Sample(time, seconds, SampleStatus.Timeout, ms, ip, null);
                    return new Sample(time, seconds, SampleStatus.Error, ms, ip, DescribeError(error));
                }

                return new Sample(time, seconds, SampleStatus.Ok, ms, ip, null);
            }
        }

        // Looks the hostname up on the first attempt and then every few minutes.
        // If a later lookup fails the previous address keeps being used.
        private async Task<string> EnsureResolvedAsync()
        {
            if (_isLiteral)
                return null;
            if (_address != null && DateTime.UtcNow - _lastResolved < ReResolveInterval)
                return null;

            try
            {
                IPAddress resolved = await ResolveAsync(_host);
                _lastResolved = DateTime.UtcNow;
                if (!resolved.Equals(_address))
                {
                    _address = resolved;
                    if (!_closed)
                        UpdateTitle();
                }
                return null;
            }
            catch (Exception ex)
            {
                if (_address != null)
                    _lastResolved = DateTime.UtcNow;
                return DescribeError(ex);
            }
        }

        private static async Task<IPAddress> ResolveAsync(string host)
        {
            Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
            if (await Task.WhenAny(lookup, Task.Delay(DnsTimeoutMs)) != lookup)
            {
                IgnoreFault(lookup);
                throw new TimeoutException("DNS lookup timed out");
            }

            // Prefer IPv4, so a broken IPv6 route doesn't turn into failures
            IPAddress first = null;
            foreach (IPAddress address in await lookup)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return address;
                if (first == null)
                    first = address;
            }

            if (first == null)
                throw new InvalidOperationException("No address found");
            return first;
        }

        private static void IgnoreFault(Task task)
        {
            task.ContinueWith(t => { var unused = t.Exception; },
                              TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static string DescribeError(Exception ex)
        {
            if (ex == null)
                return "Unknown error";
            var socketError = ex as SocketException;
            return socketError != null ? socketError.SocketErrorCode.ToString() : ex.Message;
        }

        private void AddSample(Sample sample)
        {
            if (_samples.Count >= MaxSamplesKept)
            {
                _samples.RemoveRange(0, MaxSamplesKept / 10);
                _hoverIndex = -1;
            }

            _sent++;
            sample.Number = _sent;
            _samples.Add(sample);

            switch (sample.Status)
            {
                case SampleStatus.Ok:
                    break;
                case SampleStatus.Timeout:
                    _timeouts++;
                    break;
                case SampleStatus.Refused:
                    _refused++;
                    break;
                default:
                    _errors++;
                    break;
            }

            _viewEnd = Math.Max(_viewEnd, sample.Seconds);
            WriteLog(sample);
            RefreshGraph();
        }

        // ----- MOUSE -----

        private void PictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            _mouse = e.Location;
            _mouseInside = true;

            // Only redraw when the mouse moves to a different sample
            int old = _hoverIndex;
            UpdateHover();
            if (_hoverIndex != old)
                pictureBox1.Invalidate();
        }

        private void PictureBox1_MouseLeave(object sender, EventArgs e)
        {
            _mouseInside = false;
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                pictureBox1.Invalidate();
            }
        }

        private void RefreshGraph()
        {
            if (_mouseInside)
                UpdateHover();
            pictureBox1.Invalidate();
        }

        private void UpdateHover()
        {
            _hoverIndex = -1;
            if (_samples.Count == 0)
                return;

            Rectangle plot = GetPlotArea(pictureBox1.ClientSize);
            if (plot.Width <= 0 || plot.Height <= 0)
                return;
            if (_mouse.X < plot.Left - 5 || _mouse.X > plot.Right + 5 ||
                _mouse.Y < plot.Top - 5 || _mouse.Y > plot.Bottom + 5)
                return;

            double start, end;
            GetAxisRange(out start, out end);
            int first = LowerBound(start);
            if (first >= _samples.Count)
                return;

            double t = start + (_mouse.X - plot.Left) / (double)plot.Width * (end - start);
            int i = LowerBound(t);
            if (i >= _samples.Count)
                i = _samples.Count - 1;
            else if (i > 0 && t - _samples[i - 1].Seconds < _samples[i].Seconds - t)
                i--;

            _hoverIndex = Math.Max(i, first);
        }

        // First sample at or after the given time
        private int LowerBound(double seconds)
        {
            int lo = 0, hi = _samples.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_samples[mid].Seconds < seconds)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        // ----- DRAWING -----

        private void PictureBox1_Paint(object sender, PaintEventArgs e)
        {
            Render(e.Graphics, pictureBox1.ClientSize, true);
        }

        private static Rectangle GetPlotArea(Size size)
        {
            return new Rectangle(PlotLeft, PlotTop,
                                 size.Width - PlotLeft - StatsColumnWidth,
                                 size.Height - PlotTop - PlotBottomMargin);
        }

        private void GetAxisRange(out double start, out double end)
        {
            double window = _options.WindowSeconds;
            end = Math.Max(_viewEnd, Math.Min(MinAxisSeconds, window));
            start = Math.Max(0, end - window);
        }

        // Steps of 1/2/5 so the labels are round numbers, at most 10 grid lines
        private static void GetYAxis(double maxValue, out double yMax, out double step)
        {
            double target = Math.Max(MinYMax, maxValue * 1.1);
            for (double magnitude = 1; ; magnitude *= 10)
            {
                foreach (double m in new[] { 1.0, 2.0, 5.0 })
                {
                    step = m * magnitude;
                    double lines = Math.Ceiling(target / step);
                    if (lines <= 10)
                    {
                        yMax = lines * step;
                        return;
                    }
                }
            }
        }

        private static double GetTimeStep(double span, int width)
        {
            int maxLabels = Math.Max(2, width / 80);
            foreach (double step in TimeSteps)
            {
                if (span / step <= maxLabels)
                    return step;
            }
            return TimeSteps[TimeSteps.Length - 1];
        }

        private static string FormatElapsed(double seconds, bool withHours)
        {
            var ts = TimeSpan.FromSeconds(Math.Round(seconds));
            return withHours
                ? string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
                : string.Format("{0}:{1:00}", (int)ts.TotalMinutes, ts.Seconds);
        }

        private static float TimeToX(double seconds, double start, double end, Rectangle plot)
        {
            return plot.Left + (float)((seconds - start) / (end - start) * plot.Width);
        }

        private static float ValueToY(double value, double yMax, Rectangle plot)
        {
            return plot.Bottom - (float)(Math.Min(value, yMax) / yMax * plot.Height);
        }

        private void Render(Graphics g, Size size, bool includeHover)
        {
            // Enable anti-aliasing for smoother lines
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackgroundColor);

            Rectangle plot = GetPlotArea(size);
            if (plot.Width < 20 || plot.Height < 20)
                return;

            double start, end;
            GetAxisRange(out start, out end);
            int first = LowerBound(start);
            WindowStats stats = ComputeStats(first);

            double yMax, yStep;
            GetYAxis(stats.Max, out yMax, out yStep);

            Font font = Font;
            using (var gridPen = new Pen(Color.LightGray, 1))
            using (var framePen = new Pen(Color.Gray, 1))
            using (var textBrush = new SolidBrush(Color.Black))
            {
                // ----- GRID + Y LABELS -----
                for (int i = 0; i * yStep <= yMax + yStep / 2; i++)
                {
                    double v = i * yStep;
                    float y = ValueToY(v, yMax, plot);
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);

                    string label = v.ToString("0", CultureInfo.CurrentCulture);
                    SizeF labelSize = g.MeasureString(label, font);
                    g.DrawString(label, font, textBrush, plot.Left - labelSize.Width - 4, y - labelSize.Height / 2);
                }

                // ----- GRID + X LABELS -----
                double tStep = GetTimeStep(end - start, plot.Width);
                bool withHours = end >= 3600;
                for (double t = Math.Ceiling(start / tStep) * tStep; t <= end + 1e-6; t += tStep)
                {
                    float x = TimeToX(t, start, end, plot);
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);

                    string label = FormatElapsed(t, withHours);
                    SizeF labelSize = g.MeasureString(label, font);
                    float labelX = Math.Max(0, Math.Min(x - labelSize.Width / 2, size.Width - labelSize.Width));
                    g.DrawString(label, font, textBrush, labelX, plot.Bottom + 3);
                }

                g.DrawRectangle(framePen, plot);

                // ----- AXIS LABELS -----
                g.DrawString("Latency (ms)", font, textBrush, 5, 5);
                const string timeTitle = "Time since start";
                SizeF titleSize = g.MeasureString(timeTitle, font);
                g.DrawString(timeTitle, font, textBrush, plot.Left + (plot.Width - titleSize.Width) / 2, plot.Bottom + 20);
            }

            DrawSamples(g, plot, start, end, yMax, Math.Max(0, first - 1));
            DrawStats(g, size, plot, stats);

            // ----- HOVER DOT AND TOOLTIP -----
            if (includeHover && _hoverIndex >= 0 && _hoverIndex < _samples.Count)
                DrawHover(g, size, plot, start, end, yMax);
        }

        private void DrawSamples(Graphics g, Rectangle plot, double start, double end, double yMax, int from)
        {
            // A gap longer than this means samples were missing (pause, hang), so the line is broken
            double gapSeconds = (Math.Max(_options.IntervalMs, _options.TimeoutMs) + _options.IntervalMs) / 1000.0 + 1;

            Rectangle clip = plot;
            clip.Inflate(1, 1);
            g.SetClip(clip);

            var run = new List<PointF>();
            using (var linePen = new Pen(Color.Black, 2) { LineJoin = LineJoin.Round })
            using (var lineBrush = new SolidBrush(Color.Black))
            using (var timeoutPen = new Pen(TimeoutColor, 2))
            using (var refusedPen = new Pen(RefusedColor, 2))
            {
                bool hasPrevious = false;
                Sample previous = default(Sample);
                for (int i = from; i < _samples.Count; i++)
                {
                    Sample s = _samples[i];
                    float x = TimeToX(s.Seconds, start, end, plot);

                    if (s.Status == SampleStatus.Ok)
                    {
                        if (!hasPrevious || previous.Status != SampleStatus.Ok || s.Seconds - previous.Seconds > gapSeconds)
                            FlushLine(g, run, linePen, lineBrush);
                        run.Add(new PointF(x, ValueToY(s.LatencyMs, yMax, plot)));
                    }
                    else
                    {
                        // Failures leave a gap in the line and get a cross at the top
                        FlushLine(g, run, linePen, lineBrush);
                        DrawCross(g, s.Status == SampleStatus.Timeout ? timeoutPen : refusedPen, x, plot.Top + MarkerOffset, 4);
                    }

                    previous = s;
                    hasPrevious = true;
                }
                FlushLine(g, run, linePen, lineBrush);
            }

            g.ResetClip();
        }

        private static void FlushLine(Graphics g, List<PointF> run, Pen pen, Brush brush)
        {
            if (run.Count >= 2)
                g.DrawLines(pen, run.ToArray());
            else if (run.Count == 1)
                g.FillEllipse(brush, run[0].X - 2, run[0].Y - 2, 4, 4);
            run.Clear();
        }

        private static void DrawCross(Graphics g, Pen pen, float x, float y, float r)
        {
            g.DrawLine(pen, x - r, y - r, x + r, y + r);
            g.DrawLine(pen, x - r, y + r, x + r, y - r);
        }

        private void DrawHover(Graphics g, Size size, Rectangle plot, double start, double end, double yMax)
        {
            Sample s = _samples[_hoverIndex];
            float dotX = TimeToX(s.Seconds, start, end, plot);
            float dotY = s.Status == SampleStatus.Ok ? ValueToY(s.LatencyMs, yMax, plot) : plot.Top + MarkerOffset;

            // Draw vertical line from dot to bottom
            using (var hoverLinePen = new Pen(Color.Red, 1) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(hoverLinePen, dotX, dotY, dotX, plot.Bottom);
            }

            using (var dotBrush = new SolidBrush(Color.Red))
            using (var dotOutlinePen = new Pen(Color.White, 2))
            {
                const float dotRadius = 4;
                g.FillEllipse(dotBrush, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
                g.DrawEllipse(dotOutlinePen, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
            }

            var text = new StringBuilder();
            text.AppendLine("Date: " + s.Time.ToString("yyyy-MM-dd"));
            text.AppendLine("Time: " + s.Time.ToString("HH:mm:ss"));
            text.AppendLine("Latency: " + DescribeSample(s));
            if (!_isLiteral && s.Ip != null)
                text.AppendLine("IP: " + s.Ip);
            text.Append("Sample: " + s.Number);
            string tooltipText = text.ToString();

            using (var tooltipBrush = new SolidBrush(Color.Black))
            using (var tooltipBackground = new SolidBrush(Color.FromArgb(240, 255, 255, 192)))
            using (var tooltipBorder = new Pen(Color.Black, 1))
            {
                SizeF textSize = g.MeasureString(tooltipText, _tooltipFont);
                float tooltipX = dotX + 10;
                float tooltipY = dotY - textSize.Height - 10;

                // Adjust tooltip position if it goes off screen
                if (tooltipX + textSize.Width > size.Width - 10)
                    tooltipX = dotX - textSize.Width - 10;
                if (tooltipY < 10)
                    tooltipY = dotY + 10;
                tooltipX = Math.Max(5, tooltipX);
                tooltipY = Math.Min(tooltipY, size.Height - textSize.Height - 5);

                RectangleF tooltipRect = new RectangleF(tooltipX - 5, tooltipY - 5, textSize.Width + 10, textSize.Height + 10);
                g.FillRectangle(tooltipBackground, tooltipRect);
                g.DrawRectangle(tooltipBorder, Rectangle.Round(tooltipRect));
                g.DrawString(tooltipText, _tooltipFont, tooltipBrush, tooltipX, tooltipY);
            }
        }

        private void DrawStats(Graphics g, Size size, Rectangle plot, WindowStats stats)
        {
            int x = plot.Right + 15;
            int width = size.Width - x - 10;
            if (width < 80)
                return;

            int failed = _timeouts + _refused + _errors;
            double loss = _sent > 0 ? failed * 100.0 / _sent : 0;
            string last = _samples.Count > 0 ? DescribeShort(_samples[_samples.Count - 1]) : "-";

            // label == null: section header, both null: empty row
            var rows = new List<KeyValuePair<string, string>>
            {
                Row(null, "On screen"),
                Row("Last", last),
                Row("Min", stats.Count > 0 ? FormatMs(stats.Min) : "-"),
                Row("Max", stats.Count > 0 ? FormatMs(stats.Max) : "-"),
                Row("Avg", stats.Count > 0 ? FormatMs(stats.Avg) : "-"),
                Row("Jitter", stats.Count > 1 ? FormatMs(stats.Jitter) : "-"),
                Row(null, null),
                Row(null, _running ? "Since start" : "Since start (stopped)"),
                Row("Sent", _sent.ToString()),
                Row("Timeouts", _timeouts.ToString()),
                Row("Refused", _refused.ToString()),
                Row("Errors", _errors.ToString()),
                Row("Loss", loss.ToString("0.0") + " %"),
            };

            Font font = Font;
            int rowHeight = font.Height + 3;
            var box = new Rectangle(x, plot.Top, width, rows.Count * rowHeight + 10);

            using (var whiteBrush = new SolidBrush(Color.White))
            using (var blackPen = new Pen(Color.Black))
            using (var textBrush = new SolidBrush(Color.Black))
            using (var headerBrush = new SolidBrush(Color.DimGray))
            {
                g.FillRectangle(whiteBrush, box);
                g.DrawRectangle(blackPen, box);

                int y = box.Top + 5;
                foreach (var row in rows)
                {
                    if (row.Key == null && row.Value != null)
                        g.DrawString(row.Value, font, headerBrush, x + 5, y);
                    else if (row.Key != null)
                    {
                        g.DrawString(row.Key, font, textBrush, x + 5, y);
                        g.DrawString(row.Value, font, textBrush, x + 70, y);
                    }
                    y += rowHeight;
                }

                // Legend for the markers
                y = box.Bottom + 10;
                using (var timeoutPen = new Pen(TimeoutColor, 2))
                using (var refusedPen = new Pen(RefusedColor, 2))
                {
                    DrawCross(g, timeoutPen, x + 9, y + rowHeight / 2f, 4);
                    g.DrawString("Timeout", font, textBrush, x + 20, y);
                    y += rowHeight;
                    DrawCross(g, refusedPen, x + 9, y + rowHeight / 2f, 4);
                    g.DrawString("Refused / error", font, textBrush, x + 20, y);
                    y += rowHeight;
                }

                if (_logError != null)
                {
                    using (var errorBrush = new SolidBrush(Color.Red))
                    {
                        var errorRect = new RectangleF(x, y + 5, width, size.Height - y - 10);
                        g.DrawString(_logError, font, errorBrush, errorRect);
                    }
                }
            }
        }

        private static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }

        private WindowStats ComputeStats(int first)
        {
            var stats = new WindowStats();
            double sum = 0, jitterSum = 0, previous = 0;
            int jitterCount = 0;
            stats.Min = double.MaxValue;

            for (int i = first; i < _samples.Count; i++)
            {
                if (_samples[i].Status != SampleStatus.Ok)
                    continue;

                double v = _samples[i].LatencyMs;
                if (stats.Count > 0)
                {
                    jitterSum += Math.Abs(v - previous);
                    jitterCount++;
                }
                stats.Count++;
                sum += v;
                stats.Min = Math.Min(stats.Min, v);
                stats.Max = Math.Max(stats.Max, v);
                previous = v;
            }

            if (stats.Count == 0)
                stats.Min = 0;
            else
                stats.Avg = sum / stats.Count;
            stats.Jitter = jitterCount > 0 ? jitterSum / jitterCount : 0;
            return stats;
        }

        private static string FormatMs(double ms)
        {
            return (ms < 10 ? ms.ToString("0.0") : ms.ToString("0")) + " ms";
        }

        private string DescribeSample(Sample s)
        {
            switch (s.Status)
            {
                case SampleStatus.Ok:
                    return FormatMs(s.LatencyMs);
                case SampleStatus.Timeout:
                    return "Timeout (no answer in " + FormatMs(s.LatencyMs) + ")";
                case SampleStatus.Refused:
                    return "Refused after " + FormatMs(s.LatencyMs);
                case SampleStatus.DnsFailed:
                    return "DNS failed: " + s.Detail;
                default:
                    return "Error: " + s.Detail;
            }
        }

        private static string DescribeShort(Sample s)
        {
            switch (s.Status)
            {
                case SampleStatus.Ok: return FormatMs(s.LatencyMs);
                case SampleStatus.Timeout: return "Timeout";
                case SampleStatus.Refused: return "Refused";
                case SampleStatus.DnsFailed: return "DNS failed";
                default: return "Error";
            }
        }

        private void UpdateTitle()
        {
            string target = _host.Contains(":") ? "[" + _host + "]:" + _port : _host + ":" + _port;
            string ip = !_isLiteral && _address != null ? " (" + _address + ")" : "";
            string state = !_running && _runId > 0 ? " – stopped" : "";
            this.Text = "TCP Latency Graph – " + target + ip + state;
        }

        // ----- DOWNLOAD -----

        private void BtnDownload_Click(object sender, EventArgs e)
        {
            string[] extensions = { ".png", ".jpg", ".bmp", ".csv" };

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp|CSV, all samples (*.csv)|*.csv";
                dialog.FileName = SafeFileName("tcplatency_" + _host + "_" + _port + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                dialog.AddExtension = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string path = dialog.FileName;
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".jpeg")
                    extension = ".jpg";
                if (Array.IndexOf(extensions, extension) < 0)
                {
                    extension = extensions[Math.Max(0, dialog.FilterIndex - 1)];
                    path += extension;
                }

                try
                {
                    if (extension == ".csv")
                        ExportCsv(path);
                    else
                        ExportImage(path, extension == ".jpg" ? ImageFormat.Jpeg : extension == ".bmp" ? ImageFormat.Bmp : ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the file:\n" + ex.Message, "Download",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string SafeFileName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { ':', '.' };
            var result = new StringBuilder(name.Length);
            foreach (char c in name)
                result.Append(invalid.Contains(c) ? '-' : c);
            return result.ToString();
        }

        private void ExportImage(string path, ImageFormat format)
        {
            Size size = pictureBox1.ClientSize;
            if (size.Width < 100 || size.Height < 100)
                size = new Size(1200, 450);

            using (var bitmap = new Bitmap(size.Width, size.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    Render(g, size, false);
                }
                bitmap.Save(path, format);
            }
        }

        private void ExportCsv(string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(Csv.Header);
                foreach (Sample s in _samples)
                    writer.WriteLine(ToCsv(s));
            }
        }

        private string ToCsv(Sample s)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return string.Join(",",
                s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
                Csv.Escape(_host),
                _port.ToString(inv),
                Csv.Escape(s.Ip),
                s.Status == SampleStatus.Ok ? s.LatencyMs.ToString("0.0", inv) : "",
                s.Status.ToString(),
                Csv.Escape(s.Detail));
        }

        // ----- LOG FILE -----

        private void OpenLog()
        {
            if (string.IsNullOrEmpty(_options.LogPath))
                return;

            try
            {
                _log = SharedLog.Acquire(_options.LogPath);
            }
            catch (Exception ex)
            {
                _logError = "Can't open log file: " + ex.Message;
            }
        }

        private void WriteLog(Sample s)
        {
            if (_log == null)
                return;

            try
            {
                _log.WriteLine(ToCsv(s));
            }
            catch (Exception ex)
            {
                _logError = "Log file write failed, logging stopped: " + ex.Message;
                CloseLog();
            }
        }

        private void CloseLog()
        {
            if (_log != null)
            {
                _log.Release();
                _log = null;
            }
        }

        // ----- CLEANUP -----

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Stop timer and clean up
            _closed = true;
            _runId++;
            _timer.Stop();
            CloseLog();

            base.OnFormClosed(e);

            if (Application.OpenForms.Count == 0)
                Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _tooltipFont?.Dispose();

                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

        private struct WindowStats
        {
            public int Count;
            public double Min, Max, Avg, Jitter;
        }
    }

    internal enum SampleStatus
    {
        Ok,
        Timeout,
        Refused,
        Error,
        DnsFailed
    }

    internal struct Sample
    {
        public readonly DateTime Time;
        public readonly double Seconds;       // since the graph was opened, used for the X axis
        public readonly SampleStatus Status;
        public readonly double LatencyMs;     // for failures: how long until it failed
        public readonly string Ip;
        public readonly string Detail;
        public int Number;

        public Sample(DateTime time, double seconds, SampleStatus status, double latencyMs, string ip, string detail)
        {
            Time = time;
            Seconds = seconds;
            Status = status;
            LatencyMs = latencyMs;
            Ip = ip;
            Detail = detail;
            Number = 0;
        }
    }
}
