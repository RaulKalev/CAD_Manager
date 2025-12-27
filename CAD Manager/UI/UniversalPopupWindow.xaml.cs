using System;
using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace CAD_Manager.UI
{
    public partial class UniversalPopupWindow : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;

        public UniversalPopupWindow()
        {
            InitializeComponent();
            _result = MessageBoxResult.Cancel; // Default to Cancel if closed via X
        }

        public MessageBoxResult Result => _result;

        public static MessageBoxResult Show(string message, string title = "Notification", 
            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information, Window owner = null)
        {
            // Execute on UI thread
            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return (MessageBoxResult)Application.Current.Dispatcher.Invoke(new Func<MessageBoxResult>(() => 
                    Show(message, title, buttons, icon, owner)));
            }

            var window = new UniversalPopupWindow
            {
                Owner = owner ?? Application.Current?.MainWindow,
                TitleText = { Text = title },
                MessageText = { Text = message }
            };

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
                    window.MessageIcon.Foreground = System.Windows.Media.Brushes.Red;
                    break;
                case MessageBoxImage.Warning:
                    window.MessageIcon.Kind = PackIconKind.Alert;
                    window.MessageIcon.Foreground = System.Windows.Media.Brushes.Orange;
                    break;
                case MessageBoxImage.Question:
                    window.MessageIcon.Kind = PackIconKind.HelpCircle;
                    window.MessageIcon.Foreground = System.Windows.Media.Brushes.CornflowerBlue;
                    break;
                case MessageBoxImage.Information:
                default:
                    window.MessageIcon.Kind = PackIconKind.Information;
                    window.MessageIcon.Foreground = (System.Windows.Media.Brush)Application.Current?.Resources["AccentBrush"] 
                        ?? System.Windows.Media.Brushes.DodgerBlue;
                    break;
            }

            // Set Buttons
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    window.Button3.Visibility = Visibility.Visible;
                    window.Button3.Content = "OK";
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.OK; window.Close(); };
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.OK; window.Close(); };
                    break;

                case MessageBoxButton.OKCancel:
                    window.Button3.Visibility = Visibility.Visible;
                    window.Button3.Content = "OK";
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.OK; window.Close(); };
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.OK; window.Close(); };

                    window.Button2.Visibility = Visibility.Visible;
                    window.Button2.Content = "Cancel";
                    window.Button2.Click += (s, e) => { window._result = MessageBoxResult.Cancel; window.Close(); };
                    break;

                case MessageBoxButton.YesNo:
                case MessageBoxButton.YesNoCancel:
                    window.Button3.Visibility = Visibility.Visible;
                    window.Button3.Content = "Yes";
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.Yes; window.Close(); };
                    window.Button3.Content = "Yes";
                    window.Button3.Click += (s, e) => { window._result = MessageBoxResult.Yes; window.Close(); };

                    window.Button2.Visibility = Visibility.Visible;
                    window.Button2.Content = "No";
                    window.Button2.Click += (s, e) => { window._result = MessageBoxResult.No; window.Close(); };

                    if (buttons == MessageBoxButton.YesNoCancel)
                    {
                        window.Button1.Visibility = Visibility.Visible;
                        window.Button1.Content = "Cancel";
                        window.Button1.Click += (s, e) => { window._result = MessageBoxResult.Cancel; window.Close(); };
                    }
                    break;
            }

            window.ShowDialog();
            return window.Result;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
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

            // Fallback: If no resources inherited, load DarkTheme by default
            if (!resourcesLoaded)
            {
                try
                {
                    var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                    var themeUri = $"pack://application:,,,/{assemblyName};component/Themes/DarkTheme.xaml";
                    var resourceDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };
                    this.Resources.MergedDictionaries.Add(resourceDict);
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
