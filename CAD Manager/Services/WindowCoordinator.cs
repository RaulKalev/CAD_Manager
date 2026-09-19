using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CAD_Manager.Services
{
    /// <summary>
    /// Owns modeless secondary windows and guarantees one live instance per key.
    /// </summary>
    public sealed class WindowCoordinator : IDisposable
    {
        private readonly Window _owner;
        private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>();
        private bool _isDisposed;

        public WindowCoordinator(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public T ShowOrActivate<T>(string key, Func<T> createWindow, Action<T> initializeWindow = null)
            where T : Window
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WindowCoordinator));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A window key is required.", nameof(key));

            if (_windows.TryGetValue(key, out Window existingWindow) && existingWindow is T existing)
            {
                if (existing.WindowState == System.Windows.WindowState.Minimized)
                    existing.WindowState = System.Windows.WindowState.Normal;

                existing.Activate();
                return existing;
            }

            IInputElement returnFocus = Keyboard.FocusedElement;
            T window = createWindow();
            window.Owner = _owner;
            _windows[key] = window;
            window.Closed += (sender, args) =>
            {
                _windows.Remove(key);
                RestoreOwnerFocus(returnFocus);
            };

            initializeWindow?.Invoke(window);
            window.Show();
            return window;
        }

        private void RestoreOwnerFocus(IInputElement returnFocus)
        {
            if (_isDisposed || !_owner.IsVisible || returnFocus == null)
                return;

            UIElement focusElement = returnFocus as UIElement;
            if (focusElement == null || !focusElement.IsVisible || !focusElement.IsEnabled)
                return;

            _owner.Activate();
            focusElement.Focus();
            Keyboard.Focus(returnFocus);
        }

        public void ApplyToOpenWindows(Action<Window> action)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WindowCoordinator));

            if (action == null)
                return;

            foreach (Window window in _windows.Values.ToList())
            {
                if (window.IsLoaded)
                    action(window);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            foreach (Window window in _windows.Values.ToList())
            {
                if (window.IsVisible)
                    window.Close();
            }

            _windows.Clear();
        }
    }
}
