namespace Orbit.App.Views;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Orbit.Core.Models;
using Orbit.Core.Native;
using Orbit.Core.Services;

public partial class OverlayWindow : Window
{
    private readonly IFileIdentifier _fileIdentifier;
    private readonly ICapabilityResolver _capabilityResolver;
    private FileIdentity[] _currentFiles = Array.Empty<FileIdentity>();

    public event Action<OperationDefinition, FileIdentity[]>? OperationSelected;

    public OverlayWindow(IFileIdentifier fileIdentifier, ICapabilityResolver capabilityResolver)
    {
        InitializeComponent();

        _fileIdentifier = fileIdentifier ?? throw new ArgumentNullException(nameof(fileIdentifier));
        _capabilityResolver = capabilityResolver ?? throw new ArgumentNullException(nameof(capabilityResolver));

        Loaded += OnLoaded;
        RadialMenu.ActionCommitted += OnActionCommitted;
        RadialMenu.MenuCancelled += OnMenuCancelled;

        DragEnter += OnWindowDragEnter;
        DragOver += OnWindowDragOver;
        DragLeave += OnWindowDragLeave;
        Drop += OnWindowDrop;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var hWnd = helper.Handle;

        // Apply WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW
        var exStyle = Win32.GetWindowLongAuto(hWnd, Win32.GWL_EXSTYLE).ToInt64();
        exStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongAuto(hWnd, Win32.GWL_EXSTYLE, new IntPtr(exStyle));

        // Hook WM_MOUSEACTIVATE to return MA_NOACTIVATE
        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Win32.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private Point ScreenToDip(int screenX, int screenY)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        return new Point(screenX / scaleX, screenY / scaleY);
    }

    public void ShowAt(int screenX, int screenY, string[]? files = null)
    {
        var dipPt = ScreenToDip(screenX, screenY);

        // Center window on cursor in DIP coordinates
        double targetLeft = dipPt.X - (Width / 2.0);
        double targetTop = dipPt.Y - (Height / 2.0);

        // Clamp to monitor work area so radial wheel is never clipped off-screen across multi-monitors
        try
        {
            var hMon = Win32.MonitorFromPoint(new Win32.POINT { x = screenX, y = screenY }, Win32.MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero)
            {
                var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
                if (Win32.GetMonitorInfo(hMon, ref mi))
                {
                    var topLeftDip = ScreenToDip(mi.rcWork.left, mi.rcWork.top);
                    var bottomRightDip = ScreenToDip(mi.rcWork.right, mi.rcWork.bottom);

                    double minLeft = topLeftDip.X + 8;
                    double maxLeft = Math.Max(minLeft, bottomRightDip.X - Width - 8);
                    double minTop = topLeftDip.Y + 8;
                    double maxTop = Math.Max(minTop, bottomRightDip.Y - Height - 8);

                    targetLeft = Math.Clamp(targetLeft, minLeft, maxLeft);
                    targetTop = Math.Clamp(targetTop, minTop, maxTop);
                }
            }
        }
        catch
        {
            // Fallback to unconstrained
        }

        Left = targetLeft;
        Top = targetTop;

        if (files != null && files.Length > 0)
        {
            SetFiles(files);
        }

        Show();
    }

    public void SetFiles(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

        _currentFiles = _fileIdentifier.IdentifyMultiple(paths);
        var capabilities = _capabilityResolver.Resolve(_currentFiles);
        RadialMenu.Populate(capabilities);
    }

    public void UpdateCursorPosition(int screenX, int screenY)
    {
        var dipPt = ScreenToDip(screenX, screenY);
        double localX = dipPt.X - Left;
        double localY = dipPt.Y - Top;
        RadialMenu.UpdateHoverState(localX, localY);
    }

    public void CommitAt(int screenX, int screenY)
    {
        UpdateCursorPosition(screenX, screenY);
        RadialMenu.CommitCurrentSelection();
    }

    public void ForwardCategoryShortcut(char key)
    {
        RadialMenu.TriggerCategoryShortcut(key);
    }

    public void Dismiss()
    {
        Hide();
        _currentFiles = Array.Empty<FileIdentity>();
    }

    private void OnActionCommitted(OperationDefinition operation)
    {
        var targetFiles = _currentFiles;
        Dismiss();

        OperationSelected?.Invoke(operation, targetFiles);
    }

    private void OnMenuCancelled()
    {
        Dismiss();
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && paths.Length > 0)
            {
                SetFiles(paths);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            var pos = e.GetPosition(RadialMenu);
            RadialMenu.UpdateHoverState(pos.X, pos.Y);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && paths.Length > 0 && _currentFiles.Length == 0)
            {
                SetFiles(paths);
            }

            RadialMenu.CommitCurrentSelection();
        }

        e.Handled = true;
    }
}
