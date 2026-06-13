using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace PCBoosterPro
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!IsAdmin())
            {
                RestartAsAdmin();
                return;
            }
            Application.Run(new MainForm());
        }

        static bool IsAdmin()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        static void RestartAsAdmin()
        {
            var proc = new ProcessStartInfo();
            proc.FileName = Application.ExecutablePath;
            proc.UseShellExecute = true;
            proc.Verb = "runas";
            try { Process.Start(proc); } catch { }
            Environment.Exit(0);
        }
    }

    struct RamInfo
    {
        public float Percent;
        public float UsedGB;
        public float TotalGB;
    }

    // Custom drawn panel with rounded corners
    class RoundedPanel : Panel
    {
        int radius;
        Color borderColor;

        public RoundedPanel(int r, Color border)
        {
            radius = r;
            borderColor = border;
            this.Resize += (s, e) => this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GetRoundPath(this.ClientRectangle, radius))
            using (var brush = new SolidBrush(this.BackColor))
            using (var pen = new Pen(borderColor, 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        GraphicsPath GetRoundPath(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Custom colored progress bar
    class ColorBar : ProgressBar
    {
        Color barColor;

        public ColorBar(Color c)
        {
            barColor = c;
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetBarColor(Color c) { barColor = c; this.Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = this.ClientRectangle;
            using (var back = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            using (var path = new GraphicsPath())
            {
                int rad = r.Height / 2;
                path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
                path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
                path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                path.CloseFigure();
                e.Graphics.FillPath(back, path);
            }

            float pct = (float)this.Value / this.Maximum;
            int fillW = (int)((r.Width - 4) * pct);
            if (fillW > 0)
            {
                using (var fillBrush = new SolidBrush(barColor))
                using (var fillPath = new GraphicsPath())
                {
                    var fr = new Rectangle(2, 2, fillW, r.Height - 4);
                    int rad = fr.Height / 2;
                    fillPath.AddArc(fr.X, fr.Y, rad * 2, rad * 2, 180, 90);
                    fillPath.AddArc(fr.Right - rad * 2, fr.Y, rad * 2, rad * 2, 270, 90);
                    fillPath.AddArc(fr.Right - rad * 2, fr.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                    fillPath.AddArc(fr.X, fr.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                    fillPath.CloseFigure();
                    e.Graphics.FillPath(fillBrush, fillPath);
                }
            }
        }
    }

    class MainForm : Form
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        // Dark theme colors
        Color BG1 = Color.FromArgb(13, 17, 23);
        Color BG2 = Color.FromArgb(22, 27, 34);
        Color CARD = Color.FromArgb(33, 38, 45);
        Color CARD_BORDER = Color.FromArgb(48, 54, 61);
        Color ACCENT = Color.FromArgb(88, 166, 255);
        Color ACCENT2 = Color.FromArgb(56, 139, 253);
        Color TEXT = Color.FromArgb(230, 237, 243);
        Color SUBTEXT = Color.FromArgb(139, 148, 158);
        Color SUCCESS = Color.FromArgb(63, 185, 80);
        Color WARN = Color.FromArgb(210, 153, 34);
        Color DANGER = Color.FromArgb(248, 81, 73);

        Label cpuLabel, ramLabel;
        ColorBar cpuBar, ramBar;
        Label statusLabel;
        Button boostBtn;
        TextBox logBox;
        System.Windows.Forms.Timer statsTimer;
        bool boosting = false;
        Panel header;

        public MainForm()
        {
            this.Text = "PC Booster Pro v2.0  —  by Yash Suthar";
            this.Size = new Size(620, 780);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = BG1;
            this.ForeColor = TEXT;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildUI();
            statsTimer = new System.Windows.Forms.Timer();
            statsTimer.Interval = 1500;
            statsTimer.Tick += (s, e) => UpdateStats();
            statsTimer.Start();
        }

        void BuildUI()
        {
            // ── Header with gradient ──
            header = new Panel();
            header.Height = 80;
            header.Dock = DockStyle.Top;
            header.Paint += (s, e) =>
            {
                using (var b = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(30, 60, 114), Color.FromArgb(42, 30, 80), 45))
                    e.Graphics.FillRectangle(b, header.ClientRectangle);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var f = new Font("Segoe UI", 22, FontStyle.Bold))
                using (var b2 = new SolidBrush(Color.White))
                    e.Graphics.DrawString("\u26A1 PC Booster Pro", f, b2, 28, 20);

                using (var f2 = new Font("Segoe UI", 9))
                using (var b3 = new SolidBrush(Color.FromArgb(160, 180, 220)))
                    e.Graphics.DrawString("by Yash Suthar", f2, b3, this.ClientSize.Width - 130, 28);

                using (var f3 = new Font("Segoe UI", 7, FontStyle.Bold))
                using (var b4 = new SolidBrush(Color.FromArgb(100, 140, 200)))
                    e.Graphics.DrawString("v2.0", f3, b4, 225, 30);
            };
            this.Controls.Add(header);

            int y = header.Bottom + 12;

            // ── Stats card ──
            y = AddCard(y, "\uD83D\uDCCA  SYSTEM STATISTICS", (RoundedPanel card) =>
            {
                int cx = 24;

                cpuLabel = new Label();
                cpuLabel.Text = "CPU";
                cpuLabel.ForeColor = SUBTEXT;
                cpuLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
                cpuLabel.Location = new Point(cx, 6);
                cpuLabel.AutoSize = true;
                card.Controls.Add(cpuLabel);

                var cpuVal = new Label();
                cpuVal.Text = "0%";
                cpuVal.Name = "cpuVal";
                cpuVal.ForeColor = ACCENT;
                cpuVal.Font = new Font("Segoe UI", 18, FontStyle.Bold);
                cpuVal.Location = new Point(cx, 22);
                cpuVal.AutoSize = true;
                card.Controls.Add(cpuVal);

                cpuBar = new ColorBar(ACCENT);
                cpuBar.Location = new Point(cx + 70, 34);
                cpuBar.Width = card.Width - cx - 90;
                cpuBar.Height = 8;
                cpuBar.Minimum = 0;
                cpuBar.Maximum = 100;
                cpuBar.Value = 0;
                card.Controls.Add(cpuBar);

                ramLabel = new Label();
                ramLabel.Text = "RAM";
                ramLabel.ForeColor = SUBTEXT;
                ramLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
                ramLabel.Location = new Point(cx, 56);
                ramLabel.AutoSize = true;
                card.Controls.Add(ramLabel);

                var ramVal = new Label();
                ramVal.Text = "0%";
                ramVal.Name = "ramVal";
                ramVal.ForeColor = ACCENT;
                ramVal.Font = new Font("Segoe UI", 18, FontStyle.Bold);
                ramVal.Location = new Point(cx, 72);
                ramVal.AutoSize = true;
                card.Controls.Add(ramVal);

                ramBar = new ColorBar(ACCENT);
                ramBar.Location = new Point(cx + 70, 84);
                ramBar.Width = card.Width - cx - 90;
                ramBar.Height = 8;
                ramBar.Minimum = 0;
                ramBar.Maximum = 100;
                ramBar.Value = 0;
                card.Controls.Add(ramBar);

                card.Height = 108;
            });

            // ── Steps card ──
            y = AddCard(y + 10, "\u2699\uFE0F  OPTIMIZATION STEPS", (RoundedPanel card) =>
            {
                string[] steps = {
                    "\U0001F6D1  Kill bloat (OneDrive, Xbox, Edge Updater\u2026)",
                    "\u2699\uFE0F  Stop 10 heavy Windows services",
                    "\U0001F3AE  Enable Game Mode + disable animations",
                    "\U0001F5D1\uFE0F  Clean Temp, Prefetch & cache",
                    "\U0001F9E0  Trim RAM on all processes",
                    "\u26A1  High Performance power plan",
                    "\U0001F680  Remove startup bloat entries",
                    "\U0001F310  Flush DNS & reset Winsock",
                };
                int sy = 4;
                foreach (var step in steps)
                {
                    var lbl = new Label();
                    lbl.Text = step;
                    lbl.ForeColor = Color.FromArgb(200, 206, 214);
                    lbl.Font = new Font("Segoe UI", 9.5f);
                    lbl.Location = new Point(20, sy);
                    lbl.AutoSize = true;
                    card.Controls.Add(lbl);
                    sy += 26;
                }
                card.Height = sy + 10;
            });

            // ── Button ──
            boostBtn = new Button();
            boostBtn.Text = "\u26A1   BOOST MY PC";
            boostBtn.BackColor = Color.FromArgb(35, 134, 54);
            boostBtn.ForeColor = Color.White;
            boostBtn.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            boostBtn.FlatStyle = FlatStyle.Flat;
            boostBtn.FlatAppearance.BorderSize = 0;
            boostBtn.Cursor = Cursors.Hand;
            boostBtn.Width = 560;
            boostBtn.Height = 54;
            boostBtn.Location = new Point((this.ClientSize.Width - 560) / 2, y + 14);
            boostBtn.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            boostBtn.Click += BoostClick;
            boostBtn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    int rad = 10;
                    var r = boostBtn.ClientRectangle;
                    path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
                    path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
                    path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                    path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                    path.CloseFigure();
                    var reg = new Region(path);
                    boostBtn.Region = reg;
                }
            };
            this.Controls.Add(boostBtn);

            statusLabel = new Label();
            statusLabel.Text = "Ready \u2014 click BOOST MY PC to start";
            statusLabel.ForeColor = SUBTEXT;
            statusLabel.Font = new Font("Segoe UI", 9);
            statusLabel.Location = new Point(30, boostBtn.Bottom + 8);
            statusLabel.AutoSize = true;
            this.Controls.Add(statusLabel);

            // ── Activity log ──
            string logTitle = "  \U0001F4DD  ACTIVITY LOG";
            int logY = statusLabel.Bottom + 10;

            var logHeader = new Label();
            logHeader.Text = logTitle;
            logHeader.ForeColor = SUBTEXT;
            logHeader.Font = new Font("Segoe UI", 7, FontStyle.Bold);
            logHeader.Location = new Point(30, logY);
            logHeader.AutoSize = true;
            this.Controls.Add(logHeader);

            int logTop = logHeader.Bottom + 4;
            int logH = this.ClientSize.Height - logTop - 50;

            var logBg = new RoundedPanel(8, CARD_BORDER);
            logBg.BackColor = CARD;
            logBg.Location = new Point(20, logTop);
            logBg.Width = this.ClientSize.Width - 40;
            logBg.Height = logH;
            logBg.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(logBg);

            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.FromArgb(22, 27, 34);
            logBox.ForeColor = Color.FromArgb(139, 148, 158);
            logBox.Font = new Font("Consolas", 9);
            logBox.BorderStyle = BorderStyle.None;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Location = new Point(12, 12);
            logBox.Width = logBg.Width - 24;
            logBox.Height = logBg.Height - 24;
            logBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            logBg.Controls.Add(logBox);

            // ── Footer ──
            var footer = new Panel();
            footer.BackColor = Color.FromArgb(13, 17, 23);
            footer.Height = 36;
            footer.Dock = DockStyle.Bottom;

            var fb = new Label();
            fb.Text = "Built by Yash Suthar  \u2022  yashsuthar12ha@gmail.com";
            fb.ForeColor = Color.FromArgb(70, 80, 100);
            fb.Font = new Font("Segoe UI", 8);
            fb.Location = new Point(24, 10);
            fb.AutoSize = true;
            footer.Controls.Add(fb);

            var gh = new Label();
            gh.Text = "github.com/yashsuthar";
            gh.ForeColor = Color.FromArgb(60, 100, 160);
            gh.Font = new Font("Segoe UI", 8);
            gh.Location = new Point(this.ClientSize.Width - 160, 10);
            gh.Size = new Size(140, 16);
            gh.TextAlign = ContentAlignment.MiddleRight;
            gh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(gh);

            this.Controls.Add(footer);
        }

        int AddCard(int y, string title, Action<RoundedPanel> fill)
        {
            var card = new RoundedPanel(8, CARD_BORDER);
            card.BackColor = CARD;
            card.Location = new Point(20, y);
            card.Width = this.ClientSize.Width - 40;
            card.Height = 60;
            card.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            var ttl = new Label();
            ttl.Text = title;
            ttl.ForeColor = SUBTEXT;
            ttl.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            ttl.Location = new Point(20, 10);
            ttl.AutoSize = true;
            card.Controls.Add(ttl);

            fill(card);

            this.Controls.Add(card);
            return card.Bottom;
        }

        void UpdateStats()
        {
            try
            {
                var cpu = GetCpuUsage();
                var ram = GetRamUsage();

                foreach (Control c in this.Controls)
                {
                    if (c is RoundedPanel)
                    {
                        foreach (Control cc in c.Controls)
                        {
                            if (cc.Name == "cpuVal") cc.Text = string.Format("{0:F0}%", cpu);
                            if (cc.Name == "ramVal") cc.Text = string.Format("{0:F0}%", ram.Percent);
                        }
                    }
                }

                cpuLabel.Text = string.Format("CPU   {0:F0}%", cpu);
                ramLabel.Text = string.Format("RAM   {0:F0}%   ({1:F1} / {2:F1} GB)", ram.Percent, ram.UsedGB, ram.TotalGB);

                cpuBar.Value = (int)Math.Min(cpu, 100);
                ramBar.Value = Math.Min((int)ram.Percent, 100);

                Color cpuC = cpu > 80 ? DANGER : cpu > 50 ? WARN : ACCENT;
                Color ramC = ram.Percent > 85 ? DANGER : ram.Percent > 60 ? WARN : ACCENT;
                cpuBar.SetBarColor(cpuC);
                ramBar.SetBarColor(ramC);
            }
            catch { }
        }

        float GetCpuUsage()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
                foreach (var obj in searcher.Get())
                    return Convert.ToSingle(obj["PercentProcessorTime"]);
            }
            catch { }
            return 0;
        }

        RamInfo GetRamUsage()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    RamInfo r;
                    r.TotalGB = Convert.ToSingle(obj["TotalVisibleMemorySize"]) / 1024 / 1024;
                    float free = Convert.ToSingle(obj["FreePhysicalMemory"]) / 1024 / 1024;
                    r.UsedGB = r.TotalGB - free;
                    r.Percent = (r.UsedGB / r.TotalGB) * 100;
                    return r;
                }
            }
            catch { }
            return default(RamInfo);
        }

        void Log(string msg)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action<string>(Log), msg);
                return;
            }
            logBox.AppendText(msg + Environment.NewLine);
            logBox.SelectionStart = logBox.Text.Length;
            logBox.ScrollToCaret();
        }

        void BoostClick(object sender, EventArgs e)
        {
            if (boosting) return;
            boosting = true;
            boostBtn.Enabled = false;
            boostBtn.BackColor = Color.FromArgb(80, 80, 90);
            boostBtn.Text = "  \u23F3  Optimizing...";
            statusLabel.Text = "Optimization in progress \u2014 please wait\u2026";
            statusLabel.ForeColor = WARN;
            logBox.Clear();

            var thread = new Thread(() =>
            {
                RunBoost();
                this.Invoke(new Action(() =>
                {
                    boosting = false;
                    boostBtn.Enabled = true;
                    boostBtn.BackColor = Color.FromArgb(35, 134, 54);
                    boostBtn.Text = "\u26A1   BOOST MY PC";
                    statusLabel.Text = "\u2705  Done! Restart your PC for full effect.";
                    statusLabel.ForeColor = SUCCESS;
                }));
            });
            thread.IsBackground = true;
            thread.Start();
        }

        void RunBoost()
        {
            Log("Starting deep optimization..." + Environment.NewLine);

            float cpu1 = GetCpuUsage();
            RamInfo ram1 = GetRamUsage();
            Log(string.Format("Before  \u2014  CPU: {0:F0}%   RAM: {1:F1} / {2:F1} GB  ({3:F0}%)",
                cpu1, ram1.UsedGB, ram1.TotalGB, ram1.Percent) + Environment.NewLine);

            Log("Killing background bloat processes...");
            var killed = KillBloat();
            Log(string.Format("  {0} processes terminated{1}", killed.Count,
                killed.Count > 0 ? ": " + string.Join(", ", killed) : "") + Environment.NewLine);

            Log("Stopping heavy Windows services...");
            var stopped = StopServices();
            Log(string.Format("  {0} services stopped{1}", stopped.Count,
                stopped.Count > 0 ? ": " + string.Join(", ", stopped) : "") + Environment.NewLine);

            Log("Cleaning Temp & Prefetch folders...");
            int count = CleanTemp();
            Log(string.Format("  {0} files removed", count) + Environment.NewLine);

            Log("Trimming RAM on all running processes...");
            FreeRam();
            Log("  Completed successfully" + Environment.NewLine);

            Log("Applying performance registry tweaks...");
            ApplyRegistryTweaks();
            Log("  Game Mode ON | Animations OFF | Transparency OFF | Menu delay 0ms" + Environment.NewLine);

            Log("Activating High Performance power plan...");
            SetPowerPlan();
            Log("  Power plan applied" + Environment.NewLine);

            Log("Removing startup bloat entries...");
            DisableStartupBloat();
            Log("  Startup cleaned" + Environment.NewLine);

            Log("Flushing DNS & resetting Winsock...");
            FlushDns();
            Log("  Network stack refreshed" + Environment.NewLine);

            Thread.Sleep(1000);
            float cpu2 = GetCpuUsage();
            RamInfo ram2 = GetRamUsage();
            float saved = Math.Max(0, ram1.UsedGB - ram2.UsedGB);
            Log(string.Format("After   \u2014  CPU: {0:F0}%   RAM: {1:F1} / {2:F1} GB  ({3:F0}%)",
                cpu2, ram2.UsedGB, ram2.TotalGB, ram2.Percent) + Environment.NewLine);
            Log(string.Format("RAM freed: ~{0:F2} GB", saved) + Environment.NewLine);
            Log("Optimization complete! Restart your PC for full effect." + Environment.NewLine);
        }

        List<string> KillBloat()
        {
            var killed = new List<string>();
            string[] bloat = {
                "OneDrive", "Teams", "Spotify", "Discord", "Skype",
                "YourPhone", "SearchApp", "WidgetService", "SpeechRuntime",
                "GameBarFTServer", "XboxGameBarWidgets", "MicrosoftEdgeUpdate",
                "SecurityHealthSystray", "SgrmBroker", "WebExperienceHost",
                "PhoneExperienceHost",
            };
            foreach (var name in bloat)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        proc.Kill();
                        killed.Add(name);
                    }
                }
                catch { }
            }
            return killed;
        }

        List<string> StopServices()
        {
            var stopped = new List<string>();
            string[] services = { "SysMain", "DiagTrack", "WSearch", "MapsBroker",
                "RetailDemo", "Fax", "XblGameSave", "XboxNetApiSvc", "WbioSrvc", "wisvc" };
            foreach (var svc in services)
            {
                try
                {
                    var sc = new ServiceController(svc);
                    if (sc.Status == ServiceControllerStatus.Running ||
                        sc.Status == ServiceControllerStatus.PausePending ||
                        sc.Status == ServiceControllerStatus.StartPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                        stopped.Add(svc);
                    }
                }
                catch { }
            }
            return stopped;
        }

        int CleanTemp()
        {
            int count = 0;
            string[] dirs = {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"),
            };
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(file); count++; } catch { }
                    }
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        try { Directory.Delete(sub, true); count++; } catch { }
                    }
                }
                catch { }
            }
            return count;
        }

        void FreeRam()
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    IntPtr h = OpenProcess(PROCESS_ALL_ACCESS, false, proc.Id);
                    if (h != IntPtr.Zero)
                    {
                        EmptyWorkingSet(h);
                        CloseHandle(h);
                    }
                }
                catch { }
            }
            GC.Collect();
        }

        void ApplyRegistryTweaks()
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects")) if (k != null) k.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop\WindowMetrics")) if (k != null) k.SetValue("MinAnimate", "0", RegistryValueKind.String); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar")) { if (k != null) { k.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord); k.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord); } } } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize")) if (k != null) k.SetValue("StartupDelayInMSec", 0, RegistryValueKind.DWord); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop")) if (k != null) k.SetValue("MenuShowDelay", "0", RegistryValueKind.String); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications")) if (k != null) k.SetValue("GlobalUserDisabled", 1, RegistryValueKind.DWord); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) if (k != null) k.SetValue("EnableTransparency", 0, RegistryValueKind.DWord); } catch { }
        }

        void SetPowerPlan()
        {
            RunCmd("powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            RunCmd("powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
        }

        void FlushDns()
        {
            RunCmd("ipconfig /flushdns");
            RunCmd("netsh winsock reset");
        }

        void DisableStartupBloat()
        {
            string[] items = { "OneDrive", "Spotify", "Discord", "Teams", "Skype", "EpicGamesLauncher" };
            foreach (var item in items)
                RunCmd(string.Format(@"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""{0}"" /f", item));
        }

        void RunCmd(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd);
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.Verb = "runas";
                using (var p = Process.Start(psi))
                    if (p != null) p.WaitForExit(5000);
            }
            catch { }
        }
    }
}
