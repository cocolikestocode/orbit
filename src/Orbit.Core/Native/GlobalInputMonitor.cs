namespace Orbit.Core.Native;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Orbit.Core.Models;

public class GlobalInputMonitor : IDisposable
{
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    private readonly Win32.HookProc _mouseProc;
    private readonly Win32.HookProc _keyboardProc;

    private bool _isMouseDown;
    private Win32.POINT _mouseDownOrigin;
    private bool _isDragging;
    private bool _isShiftHeld;
    private bool _isOrbitActive;

    public event Action<int, int>? OrbitTriggered;
    public event Action<int, int>? DragUpdated;
    public event Action<int, int>? OrbitCommitted;
    public event Action? OrbitDismissed;
    public event Action? OrbitCancelled;
    public event Action<char>? CategoryShortcutPressed;

    public int DragDistanceThreshold { get; set; } = 12;

    public GlobalInputMonitor()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public void Start()
    {
        if (_mouseHook != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hMod = Win32.GetModuleHandle(curModule?.ModuleName);

        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Trace.WriteLine($"[Orbit] Failed to set mouse hook. Error code: {err}");
        }

        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Trace.WriteLine($"[Orbit] Failed to set keyboard hook. Error code: {err}");
        }
    }

    public void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _isMouseDown = false;
        _isDragging = false;
        _isOrbitActive = false;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();

            switch (msg)
            {
                case Win32.WM_LBUTTONDOWN:
                    _isMouseDown = true;
                    _mouseDownOrigin = hookStruct.pt;
                    _isDragging = false;
                    break;

                case Win32.WM_MOUSEMOVE:
                    if (_isMouseDown)
                    {
                        var dx = hookStruct.pt.x - _mouseDownOrigin.x;
                        var dy = hookStruct.pt.y - _mouseDownOrigin.y;
                        var distSquared = dx * dx + dy * dy;

                        if (!_isDragging && distSquared >= (DragDistanceThreshold * DragDistanceThreshold))
                        {
                            _isDragging = true;
                        }

                        if (_isDragging)
                        {
                            CheckShiftState();

                            if (_isShiftHeld && !_isOrbitActive)
                            {
                                _isOrbitActive = true;
                                OrbitTriggered?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                            }
                            else if (_isOrbitActive)
                            {
                                DragUpdated?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                            }
                        }
                    }
                    break;

                case Win32.WM_LBUTTONUP:
                    if (_isOrbitActive)
                    {
                        _isOrbitActive = false;
                        OrbitCommitted?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                    }

                    _isMouseDown = false;
                    _isDragging = false;
                    break;
            }
        }

        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();

            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
            {
                if (hookStruct.vkCode == Win32.VK_SHIFT ||
                    hookStruct.vkCode == Win32.VK_LSHIFT ||
                    hookStruct.vkCode == Win32.VK_RSHIFT)
                {
                    _isShiftHeld = true;

                    if (_isDragging && !_isOrbitActive)
                    {
                        _isOrbitActive = true;
                        if (Win32.GetCursorPos(out var pt))
                        {
                            OrbitTriggered?.Invoke(pt.x, pt.y);
                        }
                    }
                }
                else if (hookStruct.vkCode == Win32.VK_ESCAPE)
                {
                    if (_isOrbitActive)
                    {
                        _isOrbitActive = false;
                        OrbitCancelled?.Invoke();
                    }
                }
                else if (_isOrbitActive)
                {
                    // Check category shortcut keys (C, E, A, O, P, X, S, I)
                    char keyChar = (char)hookStruct.vkCode;
                    if (char.IsLetter(keyChar))
                    {
                        CategoryShortcutPressed?.Invoke(char.ToUpperInvariant(keyChar));
                    }
                }
            }
            else if (msg == Win32.WM_KEYUP || msg == Win32.WM_SYSKEYUP)
            {
                if (hookStruct.vkCode == Win32.VK_SHIFT ||
                    hookStruct.vkCode == Win32.VK_LSHIFT ||
                    hookStruct.vkCode == Win32.VK_RSHIFT)
                {
                    _isShiftHeld = false;

                    if (_isOrbitActive)
                    {
                        _isOrbitActive = false;
                        OrbitDismissed?.Invoke();
                    }
                }
            }
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void CheckShiftState()
    {
        var shiftState = Win32.GetAsyncKeyState(Win32.VK_SHIFT);
        _isShiftHeld = (shiftState & 0x8000) != 0;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
