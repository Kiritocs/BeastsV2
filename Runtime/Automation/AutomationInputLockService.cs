using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime.Automation;

internal sealed class AutomationInputLockService : IDisposable
{
    private static readonly TimeSpan KeyboardAllowanceDuration = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan MouseAllowanceDuration = TimeSpan.FromMilliseconds(75);
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmMouseHWheel = 0x020E;
    private const int LlkhfInjected = 0x10;
    private const int LlmhfInjected = 0x01;

    private readonly AutomationRuntimeState _state;
    private readonly Func<bool> _isEnabled;
    private readonly Action<string> _logDebug;
    private readonly object _hookSync = new();
    private readonly object _allowanceSync = new();
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly LowLevelMouseProc _mouseProc;

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private bool _disposed;
    private volatile bool _isActive;
    private volatile int _lockedCursorX;
    private volatile int _lockedCursorY;
    private volatile bool _hasLockedCursorPosition;
    private long _allowMouseUntilUtcTicks;
    private Keys[] _allowedKeys = [];
    private readonly Dictionary<Keys, long> _temporaryAllowedKeysUntilUtcTicks = [];

    public AutomationInputLockService(AutomationRuntimeState state, Func<bool> isEnabled, Action<string> logDebug)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _logDebug = logDebug ?? (_ => { });
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public bool EnableForRun(IEnumerable<Keys> allowedKeys)
    {
        ThrowIfDisposed();

        if (!_isEnabled())
        {
            DisableForRun();
            return false;
        }

        if (!EnsureHooksInstalled())
        {
            DisableForRun();
            return false;
        }

        _allowedKeys = allowedKeys?
            .Where(key => key != Keys.None)
            .Distinct()
            .ToArray() ?? [];

        if (TryGetCursorPosition(out var cursorX, out var cursorY))
        {
            _lockedCursorX = cursorX;
            _lockedCursorY = cursorY;
            _hasLockedCursorPosition = true;
        }

        _isActive = true;
        _state.IsInputLockActive = true;
        ApplyCursorClip();
        _logDebug($"Automation input lock enabled. allowedKeys=[{string.Join(", ", _allowedKeys.Select(key => key.ToString()))}]");
        return true;
    }

    public void DisableForRun()
    {
        _isActive = false;
        _state.IsInputLockActive = false;
        _allowedKeys = [];
        _hasLockedCursorPosition = false;
        lock (_allowanceSync)
        {
            _temporaryAllowedKeysUntilUtcTicks.Clear();
        }

        _allowMouseUntilUtcTicks = 0;
        ReleaseCursorClip();
    }

    public void AllowAutomationKeys(params Keys[] keys)
    {
        if (!_isActive || keys == null || keys.Length == 0)
        {
            return;
        }

        var allowUntilUtcTicks = DateTime.UtcNow.Add(KeyboardAllowanceDuration).Ticks;
        lock (_allowanceSync)
        {
            foreach (var key in keys)
            {
                if (key == Keys.None)
                {
                    continue;
                }

                _temporaryAllowedKeysUntilUtcTicks[key] = allowUntilUtcTicks;
            }
        }
    }

    public void AllowAutomationMouseInput()
    {
        if (!_isActive)
        {
            return;
        }

        _allowMouseUntilUtcTicks = DateTime.UtcNow.Add(MouseAllowanceDuration).Ticks;
    }

    public void TrackAutomationCursorPosition(float x, float y)
    {
        if (!_isActive)
        {
            return;
        }

        _lockedCursorX = (int)Math.Round(x);
        _lockedCursorY = (int)Math.Round(y);
        _hasLockedCursorPosition = true;
        ApplyCursorClip();
    }

    public void EnforceCursorPosition()
    {
        if (!_isActive || !_hasLockedCursorPosition)
        {
            return;
        }

        if (!TryGetCursorPosition(out var currentX, out var currentY))
        {
            return;
        }

        if (currentX == _lockedCursorX && currentY == _lockedCursorY)
        {
            return;
        }

        ApplyCursorClip();
        SetCursorPos(_lockedCursorX, _lockedCursorY);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisableForRun();

        lock (_hookSync)
        {
            Unhook(ref _keyboardHook);
            Unhook(ref _mouseHook);
            _disposed = true;
        }
    }

    private bool EnsureHooksInstalled()
    {
        lock (_hookSync)
        {
            if (_keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero)
            {
                return true;
            }

            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = GetModuleHandle(module?.ModuleName);

            if (_keyboardHook == IntPtr.Zero)
            {
                _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
            }

            if (_mouseHook == IntPtr.Zero)
            {
                _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, moduleHandle, 0);
            }

            var hooksInstalled = _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
            if (hooksInstalled)
            {
                return true;
            }

            _logDebug($"Automation input lock hooks failed to install. keyboardHook={_keyboardHook}, mouseHook={_mouseHook}, win32Error={Marshal.GetLastWin32Error()}");
            Unhook(ref _keyboardHook);
            Unhook(ref _mouseHook);
            return false;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_isActive)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var message = unchecked((int)(long)wParam);
        if (message != WmKeyDown && message != WmKeyUp && message != WmSysKeyDown && message != WmSysKeyUp)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var keyboardData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((keyboardData.flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var key = (Keys)keyboardData.vkCode;
        var allowedKeys = _allowedKeys;
        if (Array.IndexOf(allowedKeys, key) >= 0)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        if (IsKeyTemporarilyAllowed(key))
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        return (IntPtr)1;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_isActive)
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var message = unchecked((int)(long)wParam);
        if (!ShouldSuppressMouseMessage(message))
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var mouseData = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        if ((mouseData.flags & LlmhfInjected) != 0)
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        if (IsMouseTemporarilyAllowed())
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        return (IntPtr)1;
    }

    private bool IsKeyTemporarilyAllowed(Keys key)
    {
        var nowUtcTicks = DateTime.UtcNow.Ticks;
        lock (_allowanceSync)
        {
            if (!_temporaryAllowedKeysUntilUtcTicks.TryGetValue(key, out var allowUntilUtcTicks))
            {
                return false;
            }

            if (allowUntilUtcTicks >= nowUtcTicks)
            {
                return true;
            }

            _temporaryAllowedKeysUntilUtcTicks.Remove(key);
            return false;
        }
    }

    private bool IsMouseTemporarilyAllowed()
    {
        return DateTime.UtcNow.Ticks <= _allowMouseUntilUtcTicks;
    }

    private static bool ShouldSuppressMouseMessage(int message)
    {
        return message == WmMouseMove ||
               message == WmLButtonDown ||
               message == WmLButtonUp ||
               message == WmRButtonDown ||
               message == WmRButtonUp ||
               message == WmMButtonDown ||
               message == WmMButtonUp ||
               message == WmMouseWheel ||
               message == WmMouseHWheel ||
               message == WmXButtonDown ||
               message == WmXButtonUp;
    }

    private static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private void ApplyCursorClip()
    {
        if (!_isActive || !_hasLockedCursorPosition)
        {
            return;
        }

        var rect = new Rect
        {
            Left = _lockedCursorX,
            Top = _lockedCursorY,
            Right = _lockedCursorX + 1,
            Bottom = _lockedCursorY + 1,
        };

        ClipCursor(ref rect);
    }

    private static void ReleaseCursorClip()
    {
        ClipCursor(IntPtr.Zero);
    }

    private static void Unhook(ref IntPtr hookHandle)
    {
        if (hookHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            UnhookWindowsHookEx(hookHandle);
        }
        catch
        {
        }
        finally
        {
            hookHandle = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AutomationInputLockService));
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(ref Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(IntPtr lpRect);
}