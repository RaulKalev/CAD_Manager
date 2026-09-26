using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CAD_Manager.Services
{
    /// <summary>
    /// Widens the main window so DWG and layer names fit in the tree, capped at
    /// the current monitor's work area. The window only grows automatically, and
    /// stops adjusting once the user resizes it during this session.
    /// </summary>
    internal sealed class TreeAutoWidthController : IDisposable
    {
        private const int WmEnterSizeMove = 0x0231;
        private const int WmExitSizeMove = 0x0232;
        private const double LayerIndentFallback = 19;
        private const double SafetyMargin = 4;

        private readonly Window _window;
        private readonly TreeView _treeView;
        private readonly Func<IEnumerable<DWGNode>> _displayedNodes;
        private HwndSource _source;
        private double _widthAtSizeMoveStart;
        private bool _userResized;
        private bool _fitScheduled;

        public TreeAutoWidthController(
            Window window,
            TreeView treeView,
            Func<IEnumerable<DWGNode>> displayedNodes)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
            _displayedNodes = displayedNodes ?? throw new ArgumentNullException(nameof(displayedNodes));
        }

        public void Attach(HwndSource source)
        {
            if (_source != null || source == null)
                return;

            _source = source;
            _source.AddHook(WindowHook);
        }

        /// <summary>
        /// Coalesces refresh requests into one fit after the tree has been laid out.
        /// </summary>
        public void ScheduleFit()
        {
            if (_fitScheduled || _userResized)
                return;

            _fitScheduled = true;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _fitScheduled = false;
                FitToNames();
            }), DispatcherPriority.Loaded);
        }

        public void FitToNames()
        {
            if (_userResized ||
                !_window.IsLoaded ||
                _window.WindowState != WindowState.Normal ||
                _source == null)
            {
                return;
            }

            _window.UpdateLayout();

            List<DWGNode> nodes = (_displayedNodes() ?? Enumerable.Empty<DWGNode>())
                .Where(node => node != null)
                .ToList();
            if (nodes.Count == 0)
                return;

            RowMetrics dwgMetrics = null;
            RowMetrics layerMetrics = null;
            foreach (DWGNode dwgNode in nodes)
            {
                TreeViewItem dwgItem = _treeView.ItemContainerGenerator.ContainerFromItem(dwgNode) as TreeViewItem;
                if (dwgItem == null)
                    continue;

                if (dwgMetrics == null)
                    dwgMetrics = RowMetrics.TryCreate(dwgItem, dwgNode, _window);

                if (layerMetrics == null && dwgItem.IsExpanded)
                {
                    foreach (LayerNode layerNode in dwgNode.Layers ?? new List<LayerNode>())
                    {
                        TreeViewItem layerItem =
                            dwgItem.ItemContainerGenerator.ContainerFromItem(layerNode) as TreeViewItem;
                        layerMetrics = RowMetrics.TryCreate(layerItem, layerNode, _window);
                        if (layerMetrics != null)
                            break;
                    }
                }

                if (dwgMetrics != null && layerMetrics != null)
                    break;
            }

            if (dwgMetrics == null)
                return;

            if (layerMetrics == null)
                layerMetrics = dwgMetrics.Indented(LayerIndentFallback);

            double rightChrome = GetRightChrome();
            double required = 0;
            foreach (DWGNode dwgNode in nodes)
            {
                required = Math.Max(required, dwgMetrics.RequiredWidth(dwgNode.Name));
                foreach (LayerNode layerNode in dwgNode.Layers ?? new List<LayerNode>())
                    required = Math.Max(required, layerMetrics.RequiredWidth(layerNode.Name));
            }

            Rect workArea = GetWorkArea();
            double target = Math.Min(
                Math.Max(_window.MinWidth, Math.Ceiling(required + rightChrome + SafetyMargin)),
                workArea.Width);

            // Grow only: shrinking on every refresh would cause resize jitter and
            // discard a wider size restored from the previous session.
            if (target <= _window.ActualWidth + 0.5)
                return;

            _window.Width = target;
            if (_window.Left + target > workArea.Right)
                _window.Left = Math.Max(workArea.Left, workArea.Right - target);
            if (_window.Left < workArea.Left)
                _window.Left = workArea.Left;
        }

        /// <summary>
        /// Returns true when the name in this text block is cut off with an ellipsis.
        /// </summary>
        public static bool IsTextTrimmed(TextBlock textBlock)
        {
            if (textBlock == null || string.IsNullOrEmpty(textBlock.Text))
                return false;

            double available = textBlock.ActualWidth - textBlock.Padding.Left - textBlock.Padding.Right;
            return MeasureText(textBlock, textBlock.Text) > available + 0.5;
        }

        public void Dispose()
        {
            if (_source != null)
            {
                _source.RemoveHook(WindowHook);
                _source = null;
            }
        }

        private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmEnterSizeMove)
            {
                _widthAtSizeMoveStart = _window.ActualWidth;
            }
            else if (message == WmExitSizeMove &&
                     Math.Abs(_window.ActualWidth - _widthAtSizeMoveStart) > 0.5)
            {
                // Moving the window also sends these messages; only a width change
                // means the user picked a size that must not be overridden.
                _userResized = true;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Width between the tree's scroll viewport and the window's right edge,
        /// including the vertical scrollbar, borders, and margins.
        /// </summary>
        private double GetRightChrome()
        {
            ScrollContentPresenter presenter = FindDescendant<ScrollContentPresenter>(_treeView);
            FrameworkElement viewport = (FrameworkElement)presenter ?? _treeView;
            double viewportRight = viewport
                .TransformToAncestor(_window)
                .Transform(new Point(viewport.ActualWidth, 0))
                .X;
            return Math.Max(0, _window.ActualWidth - viewportRight);
        }

        private Rect GetWorkArea()
        {
            System.Drawing.Rectangle pixels =
                System.Windows.Forms.Screen.FromHandle(_source.Handle).WorkingArea;
            Matrix fromDevice = _source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point topLeft = fromDevice.Transform(new Point(pixels.Left, pixels.Top));
            Point bottomRight = fromDevice.Transform(new Point(pixels.Right, pixels.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private static double MeasureText(TextBlock textBlock, string text)
        {
            FormattedText formatted = new FormattedText(
                text ?? string.Empty,
                CultureInfo.CurrentUICulture,
                textBlock.FlowDirection,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                textBlock.FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    return match;

                T descendant = FindDescendant<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        /// <summary>
        /// Horizontal space a row uses around its name text, measured from a
        /// realized row so indentation, toggles, and buttons follow the template.
        /// </summary>
        private sealed class RowMetrics
        {
            private readonly TextBlock _nameText;
            private readonly double _textLeft;
            private readonly double _trailing;

            private RowMetrics(TextBlock nameText, double textLeft, double trailing)
            {
                _nameText = nameText;
                _textLeft = textLeft;
                _trailing = trailing;
            }

            public static RowMetrics TryCreate(TreeViewItem item, object dataItem, Window window)
            {
                if (item == null || !item.IsLoaded)
                    return null;

                // Matching the data item skips name text in nested child rows.
                TextBlock nameText = FindNameText(item, dataItem);
                if (nameText == null || !(nameText.Parent is Grid rowGrid))
                    return null;

                double textLeft = nameText
                    .TransformToAncestor(window)
                    .Transform(new Point(0, 0))
                    .X - nameText.Margin.Left;

                int textColumn = Grid.GetColumn(nameText);
                double trailing = nameText.Margin.Left + nameText.Margin.Right;
                foreach (UIElement child in rowGrid.Children)
                {
                    if (!(child is FrameworkElement element) ||
                        element.Visibility == Visibility.Collapsed ||
                        Grid.GetRow(element) != 0 ||
                        Grid.GetColumn(element) <= textColumn)
                    {
                        continue;
                    }

                    trailing += element.ActualWidth + element.Margin.Left + element.Margin.Right;
                }

                return new RowMetrics(nameText, textLeft, trailing);
            }

            public RowMetrics Indented(double indent)
            {
                return new RowMetrics(_nameText, _textLeft + indent, _trailing);
            }

            public double RequiredWidth(string name)
            {
                return _textLeft + MeasureText(_nameText, name) + _trailing;
            }

            private static TextBlock FindNameText(DependencyObject parent, object dataItem)
            {
                if (parent == null)
                    return null;

                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int index = 0; index < count; index++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                    if (child is TextBlock textBlock &&
                        ReferenceEquals(textBlock.DataContext, dataItem) &&
                        BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path?.Path == "Name")
                    {
                        return textBlock;
                    }

                    TextBlock descendant = FindNameText(child, dataItem);
                    if (descendant != null)
                        return descendant;
                }

                return null;
            }
        }
    }
}
