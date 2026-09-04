using System;
using System.Windows;
using System.Windows.Interop;
using SimpinkNative.Interop;

namespace SimpinkNative.Services
{
    public sealed class HotkeyManager : IDisposable
    {
        private readonly Window _window;
        private HwndSource? _source;
        private readonly System.Collections.Generic.List<int> _registeredIds = new();
        private bool _disposed;

        public event Action<int>? HotkeyPressed;

        public HotkeyManager(Window window)
        {
            _window = window;
            window.SourceInitialized += (_, _) =>
            {
                _source = (HwndSource)PresentationSource.FromVisual(window)!;
                _source.AddHook(WndProc);
            };
        }

        public bool RegisterHotkey(int id, uint modifiers, uint vk)
        {
            if (_disposed) return false;
            try
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                if (Win32.RegisterHotKey(hwnd, id, modifiers, vk))
                {
                    _registeredIds.Add(id);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void UnregisterHotkey(int id)
        {
            try
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                Win32.UnregisterHotKey(hwnd, id);
                _registeredIds.Remove(id);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                HotkeyPressed?.Invoke(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try { _source?.RemoveHook(WndProc); } catch { }
                foreach (var id in _registeredIds.ToArray())
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(_window).Handle;
                        Win32.UnregisterHotKey(hwnd, id);
                    }
                    catch { }
                }
                _registeredIds.Clear();
                _disposed = true;
            }
        }
    }
}
