namespace Orbit.App.Views;

using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
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

    public void ShowAt(int screenX, int screenY, string[]? files = null)
    {
        // Center window on cursor
        Left = screenX - (Width / 2.0);
        Top = screenY - (Height / 2.0);

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
        // Convert screen coordinates to local control coordinates
        double localX = screenX - Left;
        double localY = screenY - Top;
        RadialMenu.UpdateHoverState(localX, localY);
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

        if (targetFiles.Length > 0)
        {
            OperationSelected?.Invoke(operation, targetFiles);
        }
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
