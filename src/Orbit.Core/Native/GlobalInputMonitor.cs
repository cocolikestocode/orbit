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
    private bool _isModifierHeld;
    private bool _isOrbitActive;

    public event Action<int, int>? OrbitTriggered;
    public event Action<int, int>? DragUpdated;
    public event Action<int, int>? OrbitCommitted;
    public event Action? OrbitDismissed;
    public event Action? OrbitCancelled;
    public event Action<char>? CategoryShortcutPressed;

    public int DragDistanceThreshold { get; set; } = 12;
    public bool IsPaused { get; set; }
    public string ActivationModifier { get; set; } = "Shift";

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
        if (nCode >= 0 && !IsPaused)
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
                            CheckModifierState();

                            if (_isModifierHeld && !_isOrbitActive)
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
        if (nCode >= 0 && !IsPaused)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();

            if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
            {
                if (IsActivationKey(hookStruct.vkCode))
                {
                    _isModifierHeld = true;

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
                    // Check category shortcut keys
                    char keyChar = (char)hookStruct.vkCode;
                    if (char.IsLetter(keyChar))
                    {
                        CategoryShortcutPressed?.Invoke(char.ToUpperInvariant(keyChar));
                    }
                }
            }
            else if (msg == Win32.WM_KEYUP || msg == Win32.WM_SYSKEYUP)
            {
                if (IsActivationKey(hookStruct.vkCode))
                {
                    _isModifierHeld = false;

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

    private bool IsActivationKey(uint vkCode)
    {
        return ActivationModifier.ToLowerInvariant() switch
        {
            "ctrl" => vkCode is (uint)Win32.VK_CONTROL or 0xA2 or 0xA3,
            "alt" => vkCode is (uint)Win32.VK_MENU or 0xA4 or 0xA5,
            _ => vkCode is (uint)Win32.VK_SHIFT or (uint)Win32.VK_LSHIFT or (uint)Win32.VK_RSHIFT
        };
    }

    private void CheckModifierState()
    {
        int vKey = ActivationModifier.ToLowerInvariant() switch
        {
            "ctrl" => Win32.VK_CONTROL,
            "alt" => Win32.VK_MENU,
            _ => Win32.VK_SHIFT
        };

        var state = Win32.GetAsyncKeyState(vKey);
        _isModifierHeld = (state & 0x8000) != 0;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
