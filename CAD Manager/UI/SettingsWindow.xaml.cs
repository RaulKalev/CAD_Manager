using CAD_Manager.Services;
using MaterialDesignThemes.Wpf;
using System;
using System.Windows;

namespace CAD_Manager.UI
{
    public partial class SettingsWindow : Window
    {
        private bool _isDarkMode;

        public SettingsWindow(
            ThemeManager themeManager,
            bool isDarkMode,
            string storageFolder,
            bool isCustomStorageFolder)
        {
            InitializeComponent();
            themeManager?.LoadTheme(this);

            _isDarkMode = isDarkMode;
            UpdateThemePresentation();
            UpdateStorageLocation(storageFolder, isCustomStorageFolder);
        }

        public event EventHandler<ThemeSettingChangedEventArgs> ThemeSettingChanged;
        public event EventHandler SelectionFiltersRequested;
        public event EventHandler<StorageLocationRequestedEventArgs> StorageLocationRequested;

        public void UpdateThemeState(bool isDarkMode)
        {
            _isDarkMode = isDarkMode;
            UpdateThemePresentation();
        }

        public void UpdateStorageLocation(string folder, bool isCustom)
        {
            StoragePathTextBox.Text = folder ?? string.Empty;
            StorageModeText.Text = isCustom
                ? "Custom folder for this project."
                : "Project default folder.";
            StorageStatusText.Visibility = System.Windows.Visibility.Collapsed;
        }

        public void SetStorageStatus(string message, bool isError)
        {
            StorageStatusText.Text = message ?? string.Empty;
            StorageStatusText.Foreground = TryFindResource(isError ? "ErrorBrush" : "SecondaryForegroundBrush")
                as System.Windows.Media.Brush;
            StorageStatusText.Visibility = string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            UpdateThemePresentation();
            ThemeSettingChanged?.Invoke(this, new ThemeSettingChangedEventArgs(_isDarkMode));
        }

        private void UpdateThemePresentation()
        {
            if (ThemeButtonText == null || ThemeIcon == null)
                return;

            ThemeButtonText.Text = _isDarkMode ? "Dark theme" : "Light theme";
            ThemeIcon.Kind = _isDarkMode ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
            ThemeButton.ToolTip = _isDarkMode ? "Switch to light theme" : "Switch to dark theme";
        }

        private void SelectionFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            SelectionFiltersRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SaveStorageButton_Click(object sender, RoutedEventArgs e)
        {
            StorageLocationRequested?.Invoke(
                this,
                new StorageLocationRequestedEventArgs(StoragePathTextBox.Text, false));
        }

        private void UseDefaultStorageButton_Click(object sender, RoutedEventArgs e)
        {
            StorageLocationRequested?.Invoke(
                this,
                new StorageLocationRequestedEventArgs(null, true));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class ThemeSettingChangedEventArgs : EventArgs
    {
        public ThemeSettingChangedEventArgs(bool isDarkMode)
        {
            IsDarkMode = isDarkMode;
        }

        public bool IsDarkMode { get; }
    }

    public sealed class StorageLocationRequestedEventArgs : EventArgs
    {
        public StorageLocationRequestedEventArgs(string folder, bool useDefault)
        {
            Folder = folder;
            UseDefault = useDefault;
        }

        public string Folder { get; }
        public bool UseDefault { get; }
    }
}
