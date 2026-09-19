using System;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using CAD_Manager.Services;

namespace CAD_Manager.UI
{
    public partial class UniversalPopupWindow : Window
    {
        public UniversalPopupWindow()
        {
            InitializeComponent();
        }

        public static void Show(string message, string title = "Notification",
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information, Window owner = null)
        {
            // Execute on UI thread
            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    Show(message, title, buttons, icon, owner)));
                return;
            }

            var window = new UniversalPopupWindow
            {
                Owner = owner ?? Application.Current?.MainWindow,
                Title = title,
                TitleText = { Text = title },
                MessageText = { Text = message }
            };
            window.Loaded += (sender, args) => window.Button3.Focus();

            // Explicitly set startup location to CenterOwner if an owner exists
            if (window.Owner != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            // Set Icon
            switch (icon)
            {
                case MessageBoxImage.Error:
                    window.MessageIcon.Kind = PackIconKind.Error;
                    window.MessageIcon.Foreground = ResolveBrush(window, "ErrorBrush", System.Windows.Media.Brushes.Red);
                    break;
                case MessageBoxImage.Warning:
                    window.MessageIcon.Kind = PackIconKind.Alert;
                    window.MessageIcon.Foreground = ResolveBrush(window, "WarningBrush", System.Windows.Media.Brushes.Orange);
                    break;
                case MessageBoxImage.Question:
                    window.MessageIcon.Kind = PackIconKind.HelpCircle;
                    window.MessageIcon.Foreground = ResolveBrush(window, "AccentBrush", System.Windows.Media.Brushes.CornflowerBlue);
                    break;
                case MessageBoxImage.Information:
                default:
                    window.MessageIcon.Kind = PackIconKind.Information;
                    window.MessageIcon.Foreground = ResolveBrush(window, "AccentBrush", System.Windows.Media.Brushes.DodgerBlue);
                    break;
            }

            // Set Buttons
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    window.Button3.Visibility = Visibility.Visible;
                    window.ConfigureButton(window.Button3, "_OK", "OK");
                    window.Button3.Click += (s, e) => window.Close();
                    break;

                case MessageBoxButton.OKCancel:
                    window.Button3.Visibility = Visibility.Visible;
                    window.ConfigureButton(window.Button3, "_OK", "OK");
                    window.Button3.Click += (s, e) => window.Close();

                    window.Button2.Visibility = Visibility.Visible;
                    window.ConfigureButton(window.Button2, "_Cancel", "Cancel");
                    window.Button2.Click += (s, e) => window.Close();
                    break;

                case MessageBoxButton.YesNo:
                case MessageBoxButton.YesNoCancel:
                    window.Button3.Visibility = Visibility.Visible;
                    window.ConfigureButton(window.Button3, "_Yes", "Yes");
                    window.Button3.Click += (s, e) => window.Close();

                    window.Button2.Visibility = Visibility.Visible;
                    window.ConfigureButton(window.Button2, "_No", "No");
                    window.Button2.Click += (s, e) => window.Close();

                    if (buttons == MessageBoxButton.YesNoCancel)
                    {
                        window.Button1.Visibility = Visibility.Visible;
                        window.ConfigureButton(window.Button1, "_Cancel", "Cancel");
                        window.Button1.Click += (s, e) => window.Close();
                    }
                    break;
            }

            window.Show();
        }

        private void ConfigureButton(System.Windows.Controls.Button button, string content, string accessibleName)
        {
            button.Content = content;
            System.Windows.Automation.AutomationProperties.SetName(button, accessibleName);
        }

        private static System.Windows.Media.Brush ResolveBrush(
            FrameworkElement element,
            string key,
            System.Windows.Media.Brush fallback)
        {
            return element.TryFindResource(key) as System.Windows.Media.Brush ?? fallback;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Inherit theme resources from owner window
            bool resourcesLoaded = false;
            if (Owner != null && Owner.Resources.MergedDictionaries.Count > 0)
            {
                this.Resources.MergedDictionaries.Clear();
                foreach (ResourceDictionary dict in Owner.Resources.MergedDictionaries)
                {
                    this.Resources.MergedDictionaries.Add(dict);
                }
                resourcesLoaded = true;
            }

            // Fallback uses the same theme resolver so Windows High Contrast is honored.
            if (!resourcesLoaded)
            {
                try
                {
                    Resources.MergedDictionaries.Add(ThemeManager.CreateThemeDictionary(true));
                }
                catch 
                {
                    // Swallowing exception to prevent crash, effectively leaves it transparent/default
                }
            }
        }
        
        // Unused event handlers required by XAML
        private void Button1_Click(object sender, RoutedEventArgs e) { }
        private void Button2_Click(object sender, RoutedEventArgs e) { }
        private void Button3_Click(object sender, RoutedEventArgs e) { }
    }
}
