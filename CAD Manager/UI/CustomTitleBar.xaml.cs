using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CAD_Manager.Helpers;

namespace CAD_Manager.UI
{
    public partial class CustomTitleBar : UserControl
    {
        public CustomTitleBar()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Window.GetWindow(this)?.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
        private void ToggleFeatureButton_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void ToggleFeatureButton_Unchecked(object sender, RoutedEventArgs e)
        {

        }

    }
}
