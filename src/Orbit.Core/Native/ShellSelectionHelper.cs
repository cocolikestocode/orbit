namespace Orbit.Core.Native;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

public static class ShellSelectionHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32.POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private const uint GA_ROOT = 2;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;

    /// <summary>
    /// Attempts to query currently selected files from the active Windows Explorer window or Desktop.
    /// Uses Shell.Application COM automation to safely read selection without disrupting the drag session.
    /// </summary>
    public static string[] GetSelectedFiles(int screenX = 0, int screenY = 0)
    {
        var result = new List<string>();

        try
        {
            var foregroundHwnd = GetForegroundWindow();
            IntPtr ptHwnd = IntPtr.Zero;
            IntPtr rootHwnd = IntPtr.Zero;

            if (screenX != 0 || screenY != 0)
            {
                ptHwnd = WindowFromPoint(new Win32.POINT { x = screenX, y = screenY });
                if (ptHwnd != IntPtr.Zero)
                {
                    rootHwnd = GetAncestor(ptHwnd, GA_ROOT);
                }
            }

            bool isDesktopTarget = IsDesktopWindow(foregroundHwnd) || IsDesktopWindow(rootHwnd) || IsDesktopWindow(ptHwnd);

            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return Array.Empty<string>();

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return Array.Empty<string>();

            try
            {
                dynamic? windows = shell.Windows();
                if (windows != null)
                {
                    // 1. If targeting desktop, try desktop first
                    if (isDesktopTarget)
                    {
                        TryGetDesktopSelection(windows, result);
                    }

                    // 2. If no desktop files, search open Explorer windows matching HWND
                    if (result.Count == 0)
                    {
                        int count = windows.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic? window = windows.Item(i);
                                if (window == null) continue;

                                long hwnd = window.HWND;
                                bool matchesHwnd = (foregroundHwnd != IntPtr.Zero && hwnd == foregroundHwnd.ToInt64()) ||
                                                  (rootHwnd != IntPtr.Zero && hwnd == rootHwnd.ToInt64()) ||
                                                  (ptHwnd != IntPtr.Zero && hwnd == ptHwnd.ToInt64());

                                if (matchesHwnd)
                                {
                                    ExtractSelectedItems(window, result);
                                    if (result.Count > 0) break;
                                }
                            }
                            catch
                            {
                                // Continue checking remaining windows
                            }
                        }
                    }

                    // 3. Fallback: if not found by exact HWND, check if any open window has selected items
                    if (result.Count == 0)
                    {
                        int count = windows.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic? window = windows.Item(i);
                                if (window != null)
                                {
                                    ExtractSelectedItems(window, result);
                                    if (result.Count > 0) break;
                                }
                            }
                            catch
                            {
                                // Continue
                            }
                        }
                    }

                    // 4. Fallback: check desktop if still empty
                    if (result.Count == 0)
                    {
                        TryGetDesktopSelection(windows, result);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            // Silently fall back if COM query fails
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Legacy compatibility overload.
    /// </summary>
    public static string[] GetSelectedFilesFromForegroundExplorer()
    {
        return GetSelectedFiles(0, 0);
    }

    private static void TryGetDesktopSelection(object windows, List<string> result)
    {
        try
        {
            object[] args = new object[] { 0, 0, SWC_DESKTOP, 0, SWFO_NEEDDISPATCH };
            var desktopDisp = windows.GetType().InvokeMember("FindWindowSW", BindingFlags.InvokeMethod, null, windows, args);
            if (desktopDisp != null)
            {
                ExtractSelectedItems(desktopDisp, result);
            }
        }
        catch
        {
            // Fallback: desktop may not be accessible via FindWindowSW in certain sandbox modes
        }
    }

    private static void ExtractSelectedItems(dynamic windowOrView, List<string> result)
    {
        try
        {
            dynamic? doc = windowOrView.Document;
            if (doc != null)
            {
                dynamic? selectedItems = doc.SelectedItems();
                if (selectedItems != null)
                {
                    int selCount = selectedItems.Count;
                    for (int s = 0; s < selCount; s++)
                    {
                        dynamic? item = selectedItems.Item(s);
                        if (item != null)
                        {
                            string path = (string)item.Path;
                            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                            {
                                result.Add(path);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore extraction errors on individual items
        }
    }

    private static bool IsDesktopWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(256);
        if (GetClassName(hWnd, sb, sb.Capacity) > 0)
        {
            var className = sb.ToString();
            return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
