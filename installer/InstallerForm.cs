using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SRPRPInstaller
{
    public partial class InstallerForm : Form
    {
        private const string LAUNCHER_URL = "https://github.com/Bloody965/srp-rp-launcher/releases/download/v1.0.0/ApocalypseLauncher.zip";
        private const string LAUNCHER_NAME = "SRP-RP.exe";

        private static readonly Color BaseBackground = Color.FromArgb(11, 8, 6);
        private static readonly Color PanelBackground = Color.FromArgb(26, 18, 14);
        private static readonly Color PanelBackgroundAlt = Color.FromArgb(33, 23, 18);
        private static readonly Color BorderColor = Color.FromArgb(112, 72, 40);
        private static readonly Color AccentPrimary = Color.FromArgb(222, 106, 43);
        private static readonly Color AccentHover = Color.FromArgb(248, 142, 75);
        private static readonly Color AccentMuted = Color.FromArgb(152, 102, 59);
        private static readonly Color TextPrimary = Color.FromArgb(247, 234, 217);
        private static readonly Color TextSecondary = Color.FromArgb(214, 185, 154);
        private static readonly Color TextMuted = Color.FromArgb(153, 126, 102);

        private Panel rootPanel;
        private Panel heroPanel;
        private Panel pathPanel;
        private Panel actionPanel;
        private Panel progressShell;
        private Label titleLabel;
        private Label subtitleLabel;
        private Label briefingLabel;
        private Label pathLabel;
        private TextBox pathTextBox;
        private Button browseButton;
        private Button installButton;
        private Label statusLabel;
        private Label footerLabel;
        private ProgressBar progressBar;
        private Timer fadeInTimer;
        private float opacity = 0f;
        private string installPath;

        public InstallerForm()
        {
            InitializeComponent();

            installPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SRP-RP-Launcher");

            pathTextBox.Text = installPath;

            Opacity = 0;
            fadeInTimer = new Timer { Interval = 18 };
            fadeInTimer.Tick += FadeIn_Tick;
            fadeInTimer.Start();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "SRP-RP Launcher Installer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(960, 620);
            DoubleBuffered = true;
            BackColor = BaseBackground;

            rootPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(26)
            };
            Controls.Add(rootPanel);

            var closeButton = CreateButton("X", new Rectangle(878, 18, 38, 38), AccentMuted, TextSecondary, 12f, FontStyle.Bold);
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.MouseEnter += (s, e) => closeButton.BackColor = Color.FromArgb(190, 88, 67);
            closeButton.MouseLeave += (s, e) => closeButton.BackColor = AccentMuted;
            closeButton.Click += CloseButton_Click;
            rootPanel.Controls.Add(closeButton);

            heroPanel = CreateSurface(new Rectangle(24, 24, 540, 510), 28);
            heroPanel.Paint += HeroPanel_Paint;
            rootPanel.Controls.Add(heroPanel);

            var badgeLabel = CreateLabel("SURVIVAL DEPLOYMENT NODE", new Rectangle(28, 28, 360, 24), 10f, FontStyle.Bold, AccentHover);
            badgeLabel.TextAlign = ContentAlignment.MiddleLeft;
            heroPanel.Controls.Add(badgeLabel);

            titleLabel = CreateLabel("SRP-RP\nFIELD INSTALLER", new Rectangle(28, 70, 420, 132), 31f, FontStyle.Bold, TextPrimary);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            heroPanel.Controls.Add(titleLabel);

            subtitleLabel = CreateLabel("POST-APOCALYPSE ACCESS PACKAGE", new Rectangle(30, 208, 420, 28), 11f, FontStyle.Bold, AccentHover);
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            heroPanel.Controls.Add(subtitleLabel);

            briefingLabel = CreateLabel(
                "Установщик теперь выглядит как полевой терминал: тяжёлые панели, тёплые сигнальные акценты и один понятный маршрут до запуска лаунчера.",
                new Rectangle(30, 252, 460, 78),
                11f,
                FontStyle.Regular,
                TextSecondary);
            briefingLabel.TextAlign = ContentAlignment.TopLeft;
            heroPanel.Controls.Add(briefingLabel);

            heroPanel.Controls.Add(CreateInfoCard(
                "ШАГ 01",
                "Выберите точку установки",
                "Лаунчер будет размещён в локальной папке и сразу подготовлен к запуску.",
                new Rectangle(28, 360, 228, 108)));

            heroPanel.Controls.Add(CreateInfoCard(
                "ШАГ 02",
                "Скачайте сборку",
                "Установщик загрузит релиз, распакует файлы и создаст ярлык на рабочем столе.",
                new Rectangle(274, 360, 238, 108)));

            actionPanel = CreateSurface(new Rectangle(588, 76, 322, 458), 28);
            rootPanel.Controls.Add(actionPanel);

            var panelEyebrow = CreateLabel("INSTALLATION CONSOLE", new Rectangle(24, 24, 240, 22), 10f, FontStyle.Bold, AccentHover);
            actionPanel.Controls.Add(panelEyebrow);

            var panelTitle = CreateLabel("Подготовка к установке", new Rectangle(24, 54, 260, 34), 18f, FontStyle.Bold, TextPrimary);
            actionPanel.Controls.Add(panelTitle);

            var panelLead = CreateLabel(
                "Выберите каталог и запустите развёртывание. После завершения лаунчер откроется автоматически.",
                new Rectangle(24, 92, 274, 48),
                10.5f,
                FontStyle.Regular,
                TextSecondary);
            actionPanel.Controls.Add(panelLead);

            pathPanel = new Panel
            {
                Location = new Point(24, 154),
                Size = new Size(274, 122),
                BackColor = PanelBackgroundAlt
            };
            pathPanel.Paint += RoundedPanel_Paint;
            actionPanel.Controls.Add(pathPanel);

            pathLabel = CreateLabel("ПАПКА УСТАНОВКИ", new Rectangle(16, 14, 180, 18), 9.5f, FontStyle.Bold, AccentHover);
            pathPanel.Controls.Add(pathLabel);

            pathTextBox = new TextBox
            {
                Location = new Point(16, 42),
                Size = new Size(242, 32),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(19, 14, 11),
                ForeColor = TextPrimary
            };
            pathPanel.Controls.Add(pathTextBox);

            browseButton = CreateButton("ОБЗОР", new Rectangle(16, 82, 242, 28), PanelBackground, AccentHover, 10f, FontStyle.Bold);
            browseButton.FlatAppearance.BorderColor = BorderColor;
            browseButton.FlatAppearance.BorderSize = 1;
            browseButton.MouseEnter += (s, e) => browseButton.BackColor = Color.FromArgb(53, 35, 27);
            browseButton.MouseLeave += (s, e) => browseButton.BackColor = PanelBackground;
            browseButton.Click += BrowseButton_Click;
            pathPanel.Controls.Add(browseButton);

            progressShell = new Panel
            {
                Location = new Point(24, 294),
                Size = new Size(274, 84),
                BackColor = PanelBackgroundAlt
            };
            progressShell.Paint += RoundedPanel_Paint;
            actionPanel.Controls.Add(progressShell);

            var progressLabel = CreateLabel("СОСТОЯНИЕ", new Rectangle(16, 12, 180, 18), 9.5f, FontStyle.Bold, AccentHover);
            progressShell.Controls.Add(progressLabel);

            progressBar = new ProgressBar
            {
                Location = new Point(16, 38),
                Size = new Size(242, 12),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            progressShell.Controls.Add(progressBar);

            statusLabel = CreateLabel("Ожидает запуска развёртывания.", new Rectangle(16, 54, 242, 20), 9.5f, FontStyle.Regular, TextSecondary);
            progressShell.Controls.Add(statusLabel);

            installButton = CreateButton("УСТАНОВИТЬ ЛАУНЧЕР", new Rectangle(24, 396, 274, 40), AccentPrimary, Color.White, 11.5f, FontStyle.Bold);
            installButton.FlatAppearance.BorderSize = 0;
            installButton.MouseEnter += (s, e) => installButton.BackColor = AccentHover;
            installButton.MouseLeave += (s, e) => installButton.BackColor = AccentPrimary;
            installButton.Click += InstallButton_Click;
            actionPanel.Controls.Add(installButton);

            footerLabel = CreateLabel("v1.0.0  •  SRP-RP deployment build", new Rectangle(24, 579, 320, 18), 9f, FontStyle.Regular, TextMuted);
            rootPanel.Controls.Add(footerLabel);

            heroPanel.MouseDown += DragWindow_MouseDown;
            titleLabel.MouseDown += DragWindow_MouseDown;
            subtitleLabel.MouseDown += DragWindow_MouseDown;
            briefingLabel.MouseDown += DragWindow_MouseDown;

            ResumeLayout(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var backgroundBrush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(8, 6, 5),
                Color.FromArgb(19, 13, 10),
                135f);
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

            using var glowBrush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(46, 222, 106, 43),
                Color.FromArgb(0, 222, 106, 43),
                45f);
            e.Graphics.FillRectangle(glowBrush, ClientRectangle);

            using var borderPen = new Pen(Color.FromArgb(132, 89, 49), 1.6f);
            e.Graphics.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);
        }

        private Panel CreateSurface(Rectangle bounds, int radius)
        {
            var panel = new Panel
            {
                Bounds = bounds,
                BackColor = Color.Transparent
            };

            panel.Paint += (sender, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = CreateRoundedRectangle(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), radius);
                using var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, panel.Width, panel.Height),
                    PanelBackgroundAlt,
                    PanelBackground,
                    90f);
                using var borderPen = new Pen(BorderColor, 1.2f);

                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(borderPen, path);
            };

            return panel;
        }

        private Control CreateInfoCard(string eyebrow, string title, string body, Rectangle bounds)
        {
            var card = new Panel
            {
                Bounds = bounds,
                BackColor = Color.Transparent
            };

            card.Paint += RoundedPanel_Paint;

            card.Controls.Add(CreateLabel(eyebrow, new Rectangle(14, 14, bounds.Width - 28, 16), 9f, FontStyle.Bold, AccentHover));
            card.Controls.Add(CreateLabel(title, new Rectangle(14, 34, bounds.Width - 28, 24), 12f, FontStyle.Bold, TextPrimary));
            card.Controls.Add(CreateLabel(body, new Rectangle(14, 62, bounds.Width - 28, 38), 9.5f, FontStyle.Regular, TextSecondary));

            return card;
        }

        private void RoundedPanel_Paint(object sender, PaintEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 18);
            using var brush = new SolidBrush(panel.BackColor == Color.Transparent ? PanelBackgroundAlt : panel.BackColor);
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        private void HeroPanel_Paint(object sender, PaintEventArgs e)
        {
            RoundedPanel_Paint(sender, e);

            if (sender is not Panel panel)
            {
                return;
            }

            using var heatGlow = new LinearGradientBrush(
                new Rectangle(0, 0, panel.Width, panel.Height),
                Color.FromArgb(38, 255, 144, 77),
                Color.FromArgb(0, 255, 144, 77),
                30f);
            e.Graphics.FillEllipse(heatGlow, panel.Width - 260, -40, 280, 220);

            using var dustPen = new Pen(Color.FromArgb(38, 255, 196, 128), 1f);
            e.Graphics.DrawLine(dustPen, 26, panel.Height - 34, panel.Width - 26, panel.Height - 34);
        }

        private Label CreateLabel(string text, Rectangle bounds, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                Bounds = bounds,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Variable Display", size, style),
                AutoSize = false
            };
        }

        private Button CreateButton(string text, Rectangle bounds, Color backColor, Color foreColor, float size, FontStyle style)
        {
            return new Button
            {
                Text = text,
                Bounds = bounds,
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Variable Display", size, style)
            };
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void FadeIn_Tick(object sender, EventArgs e)
        {
            opacity += 0.06f;
            Opacity = opacity;

            if (opacity >= 1f)
            {
                fadeInTimer.Stop();
                fadeInTimer.Dispose();
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            var fadeOut = new Timer { Interval = 16 };
            fadeOut.Tick += (fadeSender, fadeArgs) =>
            {
                Opacity -= 0.1;
                if (Opacity <= 0)
                {
                    fadeOut.Stop();
                    Application.Exit();
                }
            };
            fadeOut.Start();
        }

        private void DragWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Выберите папку для установки лаунчера",
                SelectedPath = pathTextBox.Text,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                installPath = Path.Combine(dialog.SelectedPath, "SRP-RP-Launcher");
                pathTextBox.Text = installPath;
            }
        }

        private async void InstallButton_Click(object sender, EventArgs e)
        {
            installPath = pathTextBox.Text;

            if (string.IsNullOrWhiteSpace(installPath))
            {
                MessageBox.Show("Выберите папку для установки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            installButton.Enabled = false;
            browseButton.Enabled = false;
            pathTextBox.Enabled = false;
            progressBar.Visible = true;
            statusLabel.Text = "Подготовка пакета...";

            try
            {
                Directory.CreateDirectory(installPath);
                statusLabel.Text = "Скачивание архива...";

                var zipPath = Path.Combine(Path.GetTempPath(), "ApocalypseLauncher.zip");
                await DownloadFileAsync(LAUNCHER_URL, zipPath);

                statusLabel.Text = "Распаковка файлов...";
                progressBar.Value = 84;

                ZipFile.ExtractToDirectory(zipPath, installPath, true);
                File.Delete(zipPath);

                var oldExePath = Path.Combine(installPath, "ApocalypseLauncher.exe");
                var launcherPath = Path.Combine(installPath, LAUNCHER_NAME);
                if (File.Exists(oldExePath))
                {
                    File.Move(oldExePath, launcherPath, true);
                }

                statusLabel.Text = "Создание ярлыка...";
                progressBar.Value = 94;

                CreateDesktopShortcut(launcherPath);

                statusLabel.Text = "Развёртывание завершено.";
                progressBar.Value = 100;

                await Task.Delay(900);

                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    UseShellExecute = true
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка установки: {ex.Message}\n\nПри необходимости можно скачать лаунчер вручную из GitHub release.",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                installButton.Enabled = true;
                browseButton.Enabled = true;
                pathTextBox.Enabled = true;
                progressBar.Visible = false;
                statusLabel.Text = "Установка остановлена.";
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes > 0;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (canReportProgress)
                {
                    var progress = (int)((totalRead * 100) / totalBytes);
                    progressBar.Value = Math.Min(progress, 100);
                    statusLabel.Text = $"Скачивание: {totalRead / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB";
                }
            }
        }

        private void CreateDesktopShortcut(string targetPath)
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var shortcutPath = Path.Combine(desktopPath, "SRP-RP.lnk");

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }

                dynamic shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return;
                }

                var shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = "SRP-RP";
                shortcut.Save();

                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
            catch
            {
                // Если ярлык не создался, установка всё равно считается успешной.
            }
        }
    }
}
