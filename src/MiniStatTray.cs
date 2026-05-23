using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiniStatTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "MiniStatTray.SingleInstance", out created))
            {
                if (!created)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon[] notifyIcons = new NotifyIcon[5];
        private readonly System.Windows.Forms.Timer timer;
        private readonly SystemTimesSampler cpuSampler = new SystemTimesSampler();
        private readonly GpuPowerSampler gpuPowerSampler = new GpuPowerSampler();
        private readonly NetworkSampler networkSampler = new NetworkSampler();
        private readonly Icon[] currentIcons = new Icon[5];

        public TrayContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Task Manager", null, delegate { Process.Start("taskmgr.exe"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });

            string[] names = { "CPU", "GPU", "MEM", "DN", "UP" };
            for (int i = 0; i < notifyIcons.Length; i++)
            {
                notifyIcons[i] = new NotifyIcon
                {
                    Text = "MiniStatTray " + names[i] + " starting...",
                    ContextMenuStrip = menu,
                    Visible = true
                };
            }

            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { UpdateStatus(false); };
            timer.Start();

            UpdateStatus(true);
        }

        private void UpdateStatus(bool first)
        {
            var cpu = cpuSampler.NextValue();
            var mem = MemoryInfo.Get();
            var power = PowerInfo.Get();
            var gpuWatts = gpuPowerSampler.NextWatts();
            var net = networkSampler.NextValue();
            SetMetric(0, "CPU", cpu, Math.Round(cpu).ToString("0") + "%", cpu);
            SetMetric(1, "GPU", GpuPowerToPercent(gpuWatts), FormatWatts(gpuWatts), gpuWatts);
            SetMetric(2, "MEM", mem.UsedPercent, Math.Round(mem.UsedPercent).ToString("0") + "%", mem.UsedPercent);
            SetMetric(3, "DN", NetPercentForTray(net.DownloadBytesPerSecond), FormatBytes(net.DownloadBytesPerSecond) + "/s", net.DownloadBytesPerSecond);
            SetMetric(4, "UP", NetPercentForTray(net.UploadBytesPerSecond), FormatBytes(net.UploadBytesPerSecond) + "/s", net.UploadBytesPerSecond);
        }

        private void SetMetric(int index, string label, double percent, string value, double raw)
        {
            string text = label + " " + value;
            notifyIcons[index].Text = TrimForNotifyIcon("MiniStatTray " + text);
            ReplaceIcon(index, CreateTrayMetricIcon(index, percent, value));
        }

        private void ReplaceIcon(int index, Icon icon)
        {
            var old = currentIcons[index];
            currentIcons[index] = icon;
            notifyIcons[index].Icon = currentIcons[index];
            if (old != null)
            {
                old.Dispose();
            }
        }

        private static Icon CreateTrayMetricIcon(int index, double percent, string value)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Color metricColor = MetricColor(index, percent);
            string shortValue = ShortValue(value);

            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            using (var bg = new SolidBrush(Color.FromArgb(22, 25, 28)))
            using (var side = new SolidBrush(metricColor))
            using (var textBrush = new SolidBrush(Color.White))
            using (var valueFont = new Font("Arial Narrow", shortValue.Length > 2 ? 9.0f : 10.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var shadowBrush = new SolidBrush(Color.Black))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.FillRectangle(bg, 0, 0, 16, 16);

                int fillHeight = Math.Max(2, (int)Math.Round(16 * percent / 100.0));
                g.FillRectangle(side, 0, 16 - fillHeight, 3, fillHeight);

                SizeF size = g.MeasureString(shortValue, valueFont);
                float x = Math.Max(2, (16 - size.Width) / 2 + 1);
                float y = shortValue.Length > 2 ? 2 : 1;
                g.DrawString(shortValue, valueFont, shadowBrush, x + 1, y + 1);
                g.DrawString(shortValue, valueFont, textBrush, x, y);

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(hIcon).Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        private static string TrimForNotifyIcon(string text)
        {
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        internal static string FormatWatts(double watts)
        {
            return watts < 0 ? "--W" : string.Format("{0:0}W", watts);
        }

        internal static string FormatBytes(double bytesPerSecond)
        {
            if (bytesPerSecond < 0)
            {
                return "--";
            }

            string[] units = { "B", "K", "M", "G" };
            double value = bytesPerSecond;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? string.Format("{0:0}{1}", value, units[unit]) : string.Format("{0:0.0}{1}", value, units[unit]);
        }

        private static string ShortValue(string value)
        {
            value = value.Replace("/s", "").Replace("%", "").Replace("W", "");
            if (value.EndsWith(".0"))
            {
                value = value.Substring(0, value.Length - 2);
            }

            if (value.Length <= 3)
            {
                return value;
            }

            return value.Substring(0, 3);
        }

        private static Color MetricColor(int index, double percent)
        {
            if (index == 0 || index == 2)
            {
                return Heat(percent);
            }

            if (index == 1)
            {
                return Color.FromArgb(93, 174, 255);
            }

            if (index == 3)
            {
                return Color.FromArgb(116, 224, 137);
            }

            return Color.FromArgb(255, 184, 82);
        }

        private static double GpuPowerToPercent(double watts)
        {
            if (watts < 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, watts / 240.0 * 100.0));
        }

        private static double NetPercentForTray(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return 0;
            }

            const double reference = 10 * 1024 * 1024;
            return Math.Max(0, Math.Min(100, bytesPerSecond / reference * 100.0));
        }

        private static Color Heat(double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            int r;
            int g;
            if (percent < 50)
            {
                double t = percent / 50.0;
                r = (int)(52 + (230 - 52) * t);
                g = (int)(196 + (184 - 196) * t);
            }
            else
            {
                double t = (percent - 50) / 50.0;
                r = (int)(230 + (222 - 230) * t);
                g = (int)(184 + (68 - 184) * t);
            }

            return Color.FromArgb(r, g, 72);
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            for (int i = 0; i < notifyIcons.Length; i++)
            {
                notifyIcons[i].Visible = false;
                notifyIcons[i].Dispose();
                if (currentIcons[i] != null)
                {
                    currentIcons[i].Dispose();
                }
            }
            base.ExitThreadCore();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal sealed class StatusForm : Form
    {
        private readonly Label label;

        public StatusForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = false;
            BackColor = Color.FromArgb(24, 28, 32);
            ForeColor = Color.White;
            TransparencyKey = Color.Fuchsia;
            BackColor = Color.Fuchsia;
            Opacity = 1.0;
            Width = 32;
            Height = 130;

            label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = ""
            };
            Controls.Add(label);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Task Manager", null, delegate { Process.Start("taskmgr.exe"); });
            menu.Items.Add("Reattach to taskbar", null, delegate { ShowPanel(); });
            ContextMenuStrip = menu;
            label.ContextMenuStrip = menu;

            label.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };
        }

        private double cpuPercent;
        private double memPercent;
        private double gpuWatts;
        private NetworkSampler.Snapshot net;

        public void UpdateStatus(double cpuPercent, double memPercent, double gpuWatts, NetworkSampler.Snapshot net)
        {
            this.cpuPercent = cpuPercent;
            this.memPercent = memPercent;
            this.gpuWatts = gpuWatts;
            this.net = net;
            label.Text = "";

            if (cpuPercent >= 90 || memPercent >= 90)
            {
                BackColor = Color.FromArgb(18, 20, 22);
            }
            else if (cpuPercent >= 70 || memPercent >= 80)
            {
                BackColor = Color.FromArgb(18, 20, 22);
            }
            else
            {
                BackColor = Color.FromArgb(18, 20, 22);
            }

            if (Visible)
            {
                AttachToTaskbar();
            }

            Invalidate();
        }

        public void ShowPanel()
        {
            AttachToTaskbar();
            Show();
        }

        private void AttachToTaskbar()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
            {
                PositionFallback();
                return;
            }

            SetParent(Handle, taskbar);
            int style = GetWindowLong(Handle, GWL_STYLE);
            style &= ~WS_POPUP;
            style |= WS_CHILD | WS_VISIBLE;
            SetWindowLong(Handle, GWL_STYLE, style);

            RECT rect;
            GetClientRect(taskbar, out rect);
            int taskbarWidth = rect.Right - rect.Left;
            int taskbarHeight = rect.Bottom - rect.Top;
            bool vertical = taskbarHeight > taskbarWidth;

            if (vertical)
            {
                Width = Math.Max(18, Math.Min(22, taskbarWidth));
                Height = 86;
                int x = 1;
                int y = 8;
                MoveWindow(Handle, x, y, Width, Height, true);
            }
            else
            {
                Width = 104;
                Height = Math.Max(20, Math.Min(24, taskbarHeight));
                int x = Math.Max(8, taskbarWidth - Width - 260);
                int y = 0;
                MoveWindow(Handle, x, y, Width, Height, true);
            }
        }

        private void PositionFallback()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Width = 224;
            Height = 42;
            Left = area.Right - Width - 10;
            Top = area.Bottom - Height - 8;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Color.Fuchsia))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            bool vertical = Height > Width;
            if (vertical)
            {
                int x = Math.Max(1, (Width - 18) / 2);
                DrawMiniBar(e.Graphics, x, 0, "C", cpuPercent, Math.Round(cpuPercent).ToString("0"));
                DrawMiniBar(e.Graphics, x, 17, "G", GpuPercent(gpuWatts), gpuWatts < 0 ? "--" : Math.Round(gpuWatts).ToString("0"));
                DrawMiniBar(e.Graphics, x, 34, "M", memPercent, Math.Round(memPercent).ToString("0"));
                DrawMiniBar(e.Graphics, x, 51, "D", NetPercent(net.DownloadBytesPerSecond), ShortRate(net.DownloadBytesPerSecond));
                DrawMiniBar(e.Graphics, x, 68, "U", NetPercent(net.UploadBytesPerSecond), ShortRate(net.UploadBytesPerSecond));
            }
            else
            {
                DrawMiniBar(e.Graphics, 0, 2, "C", cpuPercent, Math.Round(cpuPercent).ToString("0"));
                DrawMiniBar(e.Graphics, 21, 2, "G", GpuPercent(gpuWatts), gpuWatts < 0 ? "--" : Math.Round(gpuWatts).ToString("0"));
                DrawMiniBar(e.Graphics, 42, 2, "M", memPercent, Math.Round(memPercent).ToString("0"));
                DrawMiniBar(e.Graphics, 63, 2, "D", NetPercent(net.DownloadBytesPerSecond), ShortRate(net.DownloadBytesPerSecond));
                DrawMiniBar(e.Graphics, 84, 2, "U", NetPercent(net.UploadBytesPerSecond), ShortRate(net.UploadBytesPerSecond));
            }
        }

        private static void DrawMiniBar(Graphics g, int x, int y, string labelText, double percent, string value)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Color color = Heat(percent);
            var outer = new Rectangle(x, y + 1, 18, 15);
            int fillHeight = Math.Max(1, (int)Math.Round((outer.Height - 2) * percent / 100.0));

            using (var track = new SolidBrush(Color.FromArgb(64, 70, 76)))
            using (var fill = new SolidBrush(color))
            using (var textBrush = new SolidBrush(Color.White))
            using (var borderPen = new Pen(Color.FromArgb(100, 106, 112), 1))
            using (var valueFont = new Font("Segoe UI", value.Length > 2 ? 4.3f : 5.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.FillRectangle(track, outer);
                g.FillRectangle(fill, outer.Left + 1, outer.Bottom - 1 - fillHeight, outer.Width - 2, fillHeight);
                g.DrawRectangle(borderPen, outer);

                string text = value.Length > 3 ? value.Substring(0, 3) : value;
                SizeF valueSize = g.MeasureString(text, valueFont);
                g.DrawString(text, valueFont, textBrush, x + 9 - valueSize.Width / 2, y + 5 - valueSize.Height / 2);
            }
        }

        private static void DrawBar(Graphics g, int x, int y, string labelText, double percent, string value)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Color color = Heat(percent);
            var outer = new Rectangle(x, y + 1, 28, 21);
            int fillWidth = Math.Max(2, (int)Math.Round((outer.Width - 2) * percent / 100.0));

            using (var track = new SolidBrush(Color.FromArgb(74, 80, 86)))
            using (var fill = new SolidBrush(color))
            using (var textBrush = new SolidBrush(Color.White))
            using (var labelBrush = new SolidBrush(Color.FromArgb(220, 226, 230)))
            using (var borderPen = new Pen(Color.FromArgb(105, 112, 118), 1))
            using (var valueFont = new Font("Segoe UI", value.Length > 3 ? 5.0f : 6.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var labelFont = new Font("Segoe UI", 5.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.FillRectangle(track, outer);
                g.FillRectangle(fill, outer.Left + 1, outer.Top + 1, fillWidth, outer.Height - 2);
                g.DrawRectangle(borderPen, outer);

                SizeF labelSize = g.MeasureString(labelText, labelFont);
                g.DrawString(labelText, labelFont, labelBrush, x + 2, y + 2);

                SizeF valueSize = g.MeasureString(value, valueFont);
                g.DrawString(value, valueFont, textBrush, x + outer.Width - valueSize.Width - 2, y + 10);
            }
        }

        private static void DrawGauge(Graphics g, int x, int y, string labelText, double percent, string value, string suffix)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            var rect = new Rectangle(x, y, 34, 34);
            Color color = Heat(percent);

            using (var track = new Pen(Color.FromArgb(48, 55, 61), 4))
            using (var arc = new Pen(color, 4))
            using (var textBrush = new SolidBrush(Color.White))
            using (var dimBrush = new SolidBrush(Color.FromArgb(180, 188, 194)))
            using (var valueFont = new Font("Segoe UI", value.Length > 3 ? 6.0f : 7.0f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var labelFont = new Font("Segoe UI", 5.0f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawEllipse(track, rect);
                g.DrawArc(arc, rect, -90, (float)(360 * percent / 100.0));

                string center = value + suffix;
                SizeF valueSize = g.MeasureString(center, valueFont);
                g.DrawString(center, valueFont, textBrush, x + 17 - valueSize.Width / 2, y + 11 - valueSize.Height / 2);

                SizeF labelSize = g.MeasureString(labelText, labelFont);
                g.DrawString(labelText, labelFont, dimBrush, x + 17 - labelSize.Width / 2, y + 24);
            }
        }

        private static Color Heat(double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            int r;
            int g;
            if (percent < 50)
            {
                double t = percent / 50.0;
                r = (int)(52 + (230 - 52) * t);
                g = (int)(196 + (184 - 196) * t);
            }
            else
            {
                double t = (percent - 50) / 50.0;
                r = (int)(230 + (222 - 230) * t);
                g = (int)(184 + (68 - 184) * t);
            }

            return Color.FromArgb(r, g, 72);
        }

        private static double GpuPercent(double watts)
        {
            if (watts < 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, watts / 240.0 * 100.0));
        }

        private static double NetPercent(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return 0;
            }

            const double reference = 10 * 1024 * 1024;
            return Math.Max(0, Math.Min(100, bytesPerSecond / reference * 100.0));
        }

        private static string ShortRate(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
            {
                return "0";
            }

            if (bytesPerSecond < 1024 * 1024)
            {
                return Math.Round(bytesPerSecond / 1024.0).ToString("0") + "K";
            }

            return Math.Round(bytesPerSecond / 1024.0 / 1024.0, 1).ToString("0.#") + "M";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal sealed class NetworkSampler
    {
        private ulong lastReceived;
        private ulong lastSent;
        private DateTime lastSample;
        private bool hasLast;

        public Snapshot NextValue()
        {
            ulong received;
            ulong sent;
            ReadTotals(out received, out sent);
            DateTime now = DateTime.UtcNow;

            if (!hasLast)
            {
                lastReceived = received;
                lastSent = sent;
                lastSample = now;
                hasLast = true;
                return new Snapshot();
            }

            double seconds = Math.Max(0.25, (now - lastSample).TotalSeconds);
            var snapshot = new Snapshot
            {
                DownloadBytesPerSecond = received >= lastReceived ? (received - lastReceived) / seconds : 0,
                UploadBytesPerSecond = sent >= lastSent ? (sent - lastSent) / seconds : 0
            };

            lastReceived = received;
            lastSent = sent;
            lastSample = now;
            return snapshot;
        }

        private static void ReadTotals(out ulong received, out ulong sent)
        {
            received = 0;
            sent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                try
                {
                    var stats = nic.GetIPv4Statistics();
                    received += (ulong)Math.Max(0, stats.BytesReceived);
                    sent += (ulong)Math.Max(0, stats.BytesSent);
                }
                catch
                {
                }
            }
        }

        internal struct Snapshot
        {
            public double DownloadBytesPerSecond;
            public double UploadBytesPerSecond;
        }
    }

    internal sealed class GpuPowerSampler
    {
        private bool initialized;
        private bool available = true;
        private IntPtr device;

        public double NextWatts()
        {
            if (!available)
            {
                return -1;
            }

            try
            {
                if (!initialized)
                {
                    if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out device) != 0)
                    {
                        available = false;
                        return -1;
                    }

                    initialized = true;
                }

                uint milliwatts;
                int result = nvmlDeviceGetPowerUsage(device, out milliwatts);
                return result == 0 ? milliwatts / 1000.0 : -1;
            }
            catch
            {
                available = false;
                return -1;
            }
        }

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);
    }

    internal sealed class SystemTimesSampler
    {
        private ulong lastIdle;
        private ulong lastKernel;
        private ulong lastUser;
        private bool hasLast;

        public double CurrentPercent { get; private set; }

        public double NextValue()
        {
            FILETIME idle;
            FILETIME kernel;
            FILETIME user;
            if (!GetSystemTimes(out idle, out kernel, out user))
            {
                return CurrentPercent;
            }

            ulong idleNow = ToUInt64(idle);
            ulong kernelNow = ToUInt64(kernel);
            ulong userNow = ToUInt64(user);

            if (!hasLast)
            {
                lastIdle = idleNow;
                lastKernel = kernelNow;
                lastUser = userNow;
                hasLast = true;
                CurrentPercent = 0;
                return CurrentPercent;
            }

            ulong idleDelta = idleNow - lastIdle;
            ulong kernelDelta = kernelNow - lastKernel;
            ulong userDelta = userNow - lastUser;
            ulong total = kernelDelta + userDelta;

            lastIdle = idleNow;
            lastKernel = kernelNow;
            lastUser = userNow;

            if (total == 0)
            {
                return CurrentPercent;
            }

            CurrentPercent = Math.Max(0, Math.Min(100, 100.0 * (total - idleDelta) / total));
            return CurrentPercent;
        }

        private static ulong ToUInt64(FILETIME ft)
        {
            return ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }
    }

    internal static class MemoryInfo
    {
        public static Snapshot Get()
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref status))
            {
                return new Snapshot();
            }

            var used = status.ullTotalPhys - status.ullAvailPhys;
            return new Snapshot
            {
                TotalBytes = status.ullTotalPhys,
                UsedBytes = used,
                UsedPercent = status.ullTotalPhys == 0 ? 0 : used * 100.0 / status.ullTotalPhys
            };
        }

        internal struct Snapshot
        {
            public ulong TotalBytes;
            public ulong UsedBytes;
            public double UsedPercent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }

    internal static class PowerInfo
    {
        public static Snapshot Get()
        {
            SYSTEM_POWER_STATUS status;
            if (!GetSystemPowerStatus(out status))
            {
                return new Snapshot { Text = "Power unknown", OnBattery = false };
            }

            string ac = status.ACLineStatus == 0 ? "Battery" : status.ACLineStatus == 1 ? "AC" : "Power unknown";
            string battery = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent + "%" : "no battery";
            return new Snapshot { Text = ac + ", " + battery, OnBattery = status.ACLineStatus == 0 };
        }

        internal struct Snapshot
        {
            public string Text;
            public bool OnBattery;

            public override string ToString()
            {
                return Text;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }
    }
}
