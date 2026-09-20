using CAD_Manager.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CAD_Manager.Helpers;

namespace CAD_Manager.UI
{
    public partial class ApplyToViewsWindow : Window
    {
        private readonly long _currentViewId;
        private readonly string _currentViewName;
        private readonly ViewSelectionState _selectionState;
        private ObservableCollection<ViewDescriptor> _filteredViews;
        private bool _isRequestPending;
        private bool _isUpdatingViewList;
        private DispatcherTimer _statusTimer;

        public event EventHandler<ApplyToViewsRequestedEventArgs> ApplyRequested;

        public ApplyToViewsWindow(
            IEnumerable<ViewDescriptor> views,
            long currentViewId,
            string currentViewName)
        {
            InitializeComponent();
            _currentViewId = currentViewId;
            _currentViewName = currentViewName ?? "Current View";
            _selectionState = new ViewSelectionState(
                (views ?? Enumerable.Empty<ViewDescriptor>())
                    .Where(view => view != null && view.Id != currentViewId));
            TitleText.Text = $"Apply “{_currentViewName}” to Views";
            Title = TitleText.Text;
            
            LoadViews();
            
            ViewsDataGrid.SelectionChanged += ViewsDataGrid_SelectionChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Inherit theme resources from owner window
            if (Owner != null)
            {
                this.Resources.MergedDictionaries.Clear();
                foreach (ResourceDictionary dict in Owner.Resources.MergedDictionaries)
                {
                    this.Resources.MergedDictionaries.Add(dict);
                }
            }
        }

        private void LoadViews()
        {
            _filteredViews = new ObservableCollection<ViewDescriptor>(_selectionState.AllViews);
            RestoreFilteredViewSelection();
        }

        private void ViewSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = ViewSearchBox.Text?.Trim() ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredViews = new ObservableCollection<ViewDescriptor>(_selectionState.AllViews);
            }
            else
                _filteredViews = new ObservableCollection<ViewDescriptor>(_selectionState.Filter(searchText));

            RestoreFilteredViewSelection();
        }

        private void ViewsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingViewList)
                return;

            foreach (ViewDescriptor addedItem in e.AddedItems.OfType<ViewDescriptor>())
                _selectionState.SetSelected(addedItem.Id, true);

            foreach (ViewDescriptor removedItem in e.RemovedItems.OfType<ViewDescriptor>())
                _selectionState.SetSelected(removedItem.Id, false);

            UpdateSelectionSummary();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRequestPending)
                return;

            List<long> selectedViewIds = _selectionState.SelectedIds.OrderBy(id => id).ToList();

            if (selectedViewIds.Count == 0)
                return;

            ApplyRequested?.Invoke(
                this,
                new ApplyToViewsRequestedEventArgs(
                    _currentViewId,
                    selectedViewIds));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ViewSearchBox.Focus();
            Keyboard.Focus(ViewSearchBox);
        }

        public void SetRequestState(
            bool isPending,
            string message,
            bool isError = false,
            bool autoDismiss = false)
        {
            StopStatusTimer();
            _isRequestPending = isPending;
            ResultStatusBorder.Visibility = string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            ResultStatusText.Text = isError && !string.IsNullOrWhiteSpace(message)
                ? $"Error: {message}"
                : message;
            string brushKey = isError ? "ErrorBrush" : "ForegroundBrush";
            if (TryFindResource(brushKey) != null)
                ResultStatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            else
                ResultStatusText.Foreground = SystemColors.WindowTextBrush;
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                ResultStatusText,
                isError
                    ? System.Windows.Automation.AutomationLiveSetting.Assertive
                    : System.Windows.Automation.AutomationLiveSetting.Polite);
            ResultStatusDismissButton.Visibility = isError && !isPending && !string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            if (autoDismiss && !isPending && !isError && !string.IsNullOrWhiteSpace(message))
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _statusTimer.Tick += StatusTimer_Tick;
                _statusTimer.Start();
            }

            UpdateSelectionSummary();
        }

        private void ResultStatusDismissButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRequestPending)
                HideStatus();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            HideStatus();
        }

        private void HideStatus()
        {
            StopStatusTimer();
            ResultStatusBorder.Visibility = System.Windows.Visibility.Collapsed;
            ResultStatusDismissButton.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void StopStatusTimer()
        {
            if (_statusTimer == null)
                return;

            _statusTimer.Stop();
            _statusTimer.Tick -= StatusTimer_Tick;
            _statusTimer = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopStatusTimer();
            ViewsDataGrid.SelectionChanged -= ViewsDataGrid_SelectionChanged;
            base.OnClosed(e);
        }

        private void UpdateSelectionSummary()
        {
            int count = _selectionState.SelectedIds.Count;
            int visibleSelectedCount = _selectionState.CountSelectedVisible(_filteredViews);

            SelectionSummaryText.Text = count == 0
                ? "No views selected"
                : count == 1
                    ? "1 view selected"
                    : visibleSelectedCount == count
                        ? $"{count} views selected"
                        : $"{count} views selected · {visibleSelectedCount} shown";

            ApplyButton.Content = count == 0
                ? "Select _Views to Apply"
                : count == 1
                    ? "_Apply to 1 View"
                    : $"_Apply to {count} Views";
            ApplyButton.IsEnabled = count > 0 && !_isRequestPending;
        }

        private void RestoreFilteredViewSelection()
        {
            _isUpdatingViewList = true;
            try
            {
                ViewsDataGrid.ItemsSource = _filteredViews;
                ViewsDataGrid.SelectedItems.Clear();
                foreach (ViewDescriptor viewItem in _filteredViews)
                {
                    if (_selectionState.IsSelected(viewItem.Id))
                        ViewsDataGrid.SelectedItems.Add(viewItem);
                }
            }
            finally
            {
                _isUpdatingViewList = false;
            }

            EmptyStateText.Visibility = _filteredViews.Count == 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            UpdateSelectionSummary();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }

    public sealed class ApplyToViewsRequestedEventArgs : EventArgs
    {
        public ApplyToViewsRequestedEventArgs(
            long sourceViewId,
            IReadOnlyList<long> targetViewIds)
        {
            SourceViewId = sourceViewId;
            TargetViewIds = targetViewIds;
        }

        public long SourceViewId { get; }
        public IReadOnlyList<long> TargetViewIds { get; }
    }
}
