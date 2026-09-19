using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Reflection;
using CAD_Manager.UI;

namespace CAD_Manager.Services
{
    public class ThemeManager : IDisposable
    {
        private readonly string _configFilePath;
        private readonly Window _window;
        private readonly Action<string, bool, bool> _notify;
        private bool _isDarkMode = true;
        private bool _isPinned;
        private bool _isDisposed;

        public event EventHandler ThemeResourcesChanged;

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set => _isDarkMode = value;
        }

        public bool IsPinned
        {
            get => _isPinned;
            set => _isPinned = value;
        }

        public bool IsHighContrastActive => SystemParameters.HighContrast;

        public static bool AreAnimationsEnabled => SystemParameters.ClientAreaAnimation;

        public class WindowConfig
        {
            public bool IsDarkMode { get; set; } = true;
            public bool IsPinned { get; set; }
            public double? Left { get; set; }
            public double? Top { get; set; }
            public double? Width { get; set; }
            public double? Height { get; set; }
            public string WindowState { get; set; }
        }

        public ThemeManager(Window window, Action<string, bool, bool> notify = null)
        {
            _window = window;
            _notify = notify;
            _configFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RK Tools", "CAD Manager", "config.json");
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        }

        public void LoadTheme()
        {
            LoadTheme(_window);
        }

        public void LoadTheme(Window targetWindow)
        {
            try
            {
                var resourceDict = CreateThemeDictionary(_isDarkMode);

                targetWindow.Resources.MergedDictionaries.Clear();
                targetWindow.Resources.MergedDictionaries.Add(resourceDict);
            }
            catch (Exception ex)
            {
                Notify($"Failed to load the theme: {ex.Message}", true, false);
            }
        }

        internal static ResourceDictionary CreateThemeDictionary(bool isDarkMode)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var themePath = SystemParameters.HighContrast
                ? "Themes/HighContrastTheme.xaml"
                : isDarkMode
                    ? "Themes/DarkTheme.xaml"
                    : "Themes/LightTheme.xaml";
            var themeUri = string.Format(
                "pack://application:,,,/{0};component/{1}",
                assemblyName,
                themePath);

            return new ResourceDictionary
            {
                Source = new Uri(themeUri, UriKind.Absolute)
            };
        }

        public void SaveThemeState()
        {
            try
            {
                // Persist “normal” bounds even if currently maximized/minimized
                var bounds = (_window.WindowState == WindowState.Normal)
                    ? new Rect(_window.Left, _window.Top, _window.Width, _window.Height)
                    : _window.RestoreBounds;

                var config = new WindowConfig
                {
                    IsDarkMode = _isDarkMode,
                    IsPinned = _isPinned,
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    WindowState = _window.WindowState.ToString()
                };

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath));
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Notify($"Failed to save the window state: {ex.Message}", true, false);
            }
        }

        public void LoadThemeState()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                    return;

                var json = File.ReadAllText(_configFilePath);

                // Backward compatibility with legacy theme-only file
                WindowConfig cfg = null;
                try
                {
                    cfg = JsonConvert.DeserializeObject<WindowConfig>(json);
                }
                catch
                {
                    var legacy = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                    cfg = new WindowConfig
                    {
                        IsDarkMode = legacy != null && legacy.ContainsKey("IsDarkMode") ? legacy["IsDarkMode"] : _isDarkMode
                    };
                }

                if (cfg != null)
                {
                    _isDarkMode = cfg.IsDarkMode;
                    _isPinned = cfg.IsPinned;

                    // Restore bounds if present
                    if (cfg.Left.HasValue && cfg.Top.HasValue)
                    {
                        _window.WindowStartupLocation = WindowStartupLocation.Manual;

                        // Apply size first (if available) so on-screen clamping uses correct dimensions
                        if (cfg.Width.HasValue && cfg.Width.Value > 0) _window.Width = cfg.Width.Value;
                        if (cfg.Height.HasValue && cfg.Height.Value > 0) _window.Height = cfg.Height.Value;

                        // Then position
                        _window.Left = cfg.Left.Value;
                        _window.Top = cfg.Top.Value;

                        // Keep the window on a visible screen area (multi-monitor safe)
                        EnsureOnScreen();

                        // Restore state last (avoid restoring Minimized)
                        if (!string.IsNullOrEmpty(cfg.WindowState)
                            && Enum.TryParse<System.Windows.WindowState>(cfg.WindowState, out var state)
                            && state != System.Windows.WindowState.Minimized)
                        {
                            _window.WindowState = state;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Notify($"Failed to load the window state: {ex.Message}", true, false);
            }
        }

        private void EnsureOnScreen()
        {
            // Use WPF virtual screen metrics
            double screenLeft = SystemParameters.VirtualScreenLeft;
            double screenTop = SystemParameters.VirtualScreenTop;
            double screenWidth = SystemParameters.VirtualScreenWidth;
            double screenHeight = SystemParameters.VirtualScreenHeight;

            // Nudge inside visible area with a small margin
            const double margin = 10;

            // Clamp Left/Top
            if (double.IsNaN(_window.Left)) _window.Left = screenLeft + margin;
            if (double.IsNaN(_window.Top)) _window.Top = screenTop + margin;

            _window.Left = Math.Max(screenLeft + margin, Math.Min(_window.Left, screenLeft + screenWidth - _window.Width - margin));
            _window.Top = Math.Max(screenTop + margin, Math.Min(_window.Top, screenTop + screenHeight - _window.Height - margin));
        }

        private void Notify(string message, bool isError, bool autoDismiss)
        {
            if (_notify != null)
            {
                _notify(message, isError, autoDismiss);
                return;
            }

            UniversalPopupWindow.Show(
                message,
                isError ? "Error" : "Notification",
                MessageBoxButton.OK,
                isError ? MessageBoxImage.Error : MessageBoxImage.Information,
                _window);
        }

        private void SystemParameters_StaticPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isDisposed || e.PropertyName != nameof(SystemParameters.HighContrast))
                return;

            Action reloadResources = () =>
            {
                if (_isDisposed)
                    return;

                LoadTheme();
                ThemeResourcesChanged?.Invoke(this, EventArgs.Empty);
            };

            if (_window.Dispatcher.CheckAccess())
                reloadResources();
            else
                _window.Dispatcher.BeginInvoke(reloadResources);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        }
    }
}
