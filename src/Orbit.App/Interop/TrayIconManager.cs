namespace Orbit.App.Interop;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Orbit.Core.Native;
using Orbit.Core.Services;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private readonly GlobalInputMonitor _inputMonitor;
    private readonly IJobManager _jobManager;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private bool _disposed;

    public TrayIconManager(
        MainWindow mainWindow,
        GlobalInputMonitor inputMonitor,
        IJobManager jobManager)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _inputMonitor = inputMonitor ?? throw new ArgumentNullException(nameof(inputMonitor));
        _jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));

        // Create Context Menu
        var contextMenu = new ContextMenuStrip();

        var controlCenterItem = new ToolStripMenuItem("Orbit Control Center")
        {
            Font = new Font(contextMenu.Font, FontStyle.Bold)
        };
        controlCenterItem.Click += (s, e) => RestoreMainWindow();
        contextMenu.Items.Add(controlCenterItem);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (s, e) => ShowSettingsDialog();
        contextMenu.Items.Add(settingsItem);

        var recentJobsItem = new ToolStripMenuItem("Recent Jobs");
        recentJobsItem.Click += (s, e) => ShowRecentJobs();
        contextMenu.Items.Add(recentJobsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _pauseMenuItem = new ToolStripMenuItem("Pause Orbit Gestures")
        {
            CheckOnClick = true,
            Checked = false
        };
        _pauseMenuItem.Click += (s, e) => TogglePauseGestures();
        contextMenu.Items.Add(_pauseMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        // Create NotifyIcon
        _notifyIcon = new NotifyIcon
        {
            Text = "Orbit - Global Gesture File Operations",
            Icon = CreateOrbitIcon(),
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();

        // Subscribe to job completion/failure notifications
        _jobManager.JobCompleted += OnJobCompleted;
        _jobManager.JobFailed += OnJobFailed;
    }

    public void RestoreMainWindow()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _mainWindow.WindowState = System.Windows.WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    private void ShowSettingsDialog()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var settingsWindow = (Orbit.App.Views.SettingsWindow?)App.Services?.GetService(typeof(Orbit.App.Views.SettingsWindow));
                if (settingsWindow != null)
                {
                    settingsWindow.Show();
                    settingsWindow.Activate();
                    return;
                }
            }
            catch { }

            System.Windows.MessageBox.Show(
                "Orbit Preferences\n\n" +
                "• Trigger: Shift + Drag Selection\n" +
                "• Activation Threshold: 12px drag displacement\n" +
                "• Radial Menus: Dynamic Contextual Operations\n" +
                "• Engine Acceleration: Multithreaded native execution",
                "Orbit Settings",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        });
    }

    private void ShowRecentJobs()
    {
        RestoreMainWindow();
    }

    private void TogglePauseGestures()
    {
        _inputMonitor.IsPaused = _pauseMenuItem.Checked;

        if (_inputMonitor.IsPaused)
        {
            _notifyIcon.Text = "Orbit (Gestures Paused)";
            _notifyIcon.ShowBalloonTip(1500, "Orbit Paused", "Gesture detection is temporarily paused.", ToolTipIcon.Warning);
        }
        else
        {
            _notifyIcon.Text = "Orbit - Global Gesture File Operations";
            _notifyIcon.ShowBalloonTip(1500, "Orbit Resumed", "Gesture detection is active.", ToolTipIcon.Info);
        }
    }

    private void OnJobCompleted(object? sender, JobInfo job)
    {
        var outputSummary = job.Result?.OutputFiles != null && job.Result.OutputFiles.Length > 0
            ? $"{job.Result.OutputFiles.Length} file(s) generated."
            : "Finished successfully.";

        _notifyIcon.ShowBalloonTip(
            3000,
            $"Orbit Job Completed: {job.OperationId}",
            $"{job.Request.InputFiles.Length} file(s) processed. {outputSummary}",
            ToolTipIcon.Info);
    }

    private void OnJobFailed(object? sender, JobInfo job)
    {
        var err = job.Result?.ErrorMessage ?? "An unexpected error occurred.";
        _notifyIcon.ShowBalloonTip(
            4000,
            $"Orbit Job Failed: {job.OperationId}",
            err,
            ToolTipIcon.Error);
    }

    private static Icon CreateOrbitIcon()
    {
        try
        {
            // Programmatically draw a sharp 32x32 icon with orbital rings and core
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Outer orbital ellipse
                using var orbitPen = new Pen(Color.FromArgb(200, 0, 168, 255), 2.2f);
                g.DrawEllipse(orbitPen, 3, 7, 26, 18);

                // Crossing tilted ring
                using var ringPen = new Pen(Color.FromArgb(160, 120, 60, 255), 1.8f);
                g.DrawEllipse(ringPen, 7, 3, 18, 26);

                // Glowing core circle
                using var coreBrush = new SolidBrush(Color.FromArgb(255, 0, 220, 255));
                g.FillEllipse(coreBrush, 11, 11, 10, 10);

                // Core highlight
                using var highlightBrush = new SolidBrush(Color.White);
                g.FillEllipse(highlightBrush, 13, 13, 3, 3);
            }

            var hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void ExitApplication()
    {
        Dispose();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _jobManager.JobCompleted -= OnJobCompleted;
            _jobManager.JobFailed -= OnJobFailed;

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
