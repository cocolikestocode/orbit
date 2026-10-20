namespace Orbit.Core.Native;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public static class ShellSelectionHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32.POINT Point);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Attempts to query currently selected files from the active Windows Explorer window or Desktop.
    /// Uses Shell.Application COM automation to safely read selection without disrupting the drag session.
    /// </summary>
    public static string[] GetSelectedFilesFromForegroundExplorer()
    {
        var result = new List<string>();

        try
        {
            var foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero) return Array.Empty<string>();

            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return Array.Empty<string>();

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return Array.Empty<string>();

            try
            {
                dynamic? windows = shell.Windows();
                if (windows != null)
                {
                    int count = windows.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic? window = windows.Item(i);
                            if (window == null) continue;

                            long hwnd = window.HWND;
                            if (hwnd == foregroundHwnd.ToInt64())
                            {
                                dynamic? doc = window.Document;
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
                                                if (!string.IsNullOrEmpty(path))
                                                {
                                                    result.Add(path);
                                                }
                                            }
                                        }
                                    }
                                }
                                break;
                            }
                        }
                        catch
                        {
                            // Continue checking remaining windows
                        }
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

        return result.ToArray();
    }
}
