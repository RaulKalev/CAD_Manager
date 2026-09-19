using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CAD_Manager.Helpers;

namespace CAD_Manager.UI
{
    public partial class ApplyToViewsWindow : Window
    {
        private readonly Document _document;
        private readonly View _currentView;
        private ObservableCollection<ViewItem> _allViews;
        private ObservableCollection<ViewItem> _filteredViews;
        private readonly HashSet<long> _selectedViewIds = new HashSet<long>();
        private bool _isRequestPending;
        private bool _isUpdatingViewList;

        public event EventHandler<ApplyToViewsRequestedEventArgs> ApplyRequested;

        public ApplyToViewsWindow(Document document, View currentView)
        {
            InitializeComponent();
            _document = document;
            _currentView = currentView;
            TitleText.Text = $"Apply “{_currentView.Name}” to Views";
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
            _allViews = new ObservableCollection<ViewItem>();
            
            // Collect only Floor Plan views except the current one
            FilteredElementCollector collector = new FilteredElementCollector(_document)
                .OfClass(typeof(View));

            foreach (View view in collector)
            {
                // Skip the current view, templates, and non-floor-plan views
                if (view.Id == _currentView.Id || 
                    view.IsTemplate || 
                    view.ViewType != ViewType.FloorPlan)
                    continue;

                _allViews.Add(new ViewItem
                {
                    ViewId = view.Id,
                    Name = view.Name,
                    ViewType = GetViewTypeName(view.ViewType)
                });
            }

            _filteredViews = new ObservableCollection<ViewItem>(_allViews.OrderBy(v => v.Name));
            RestoreFilteredViewSelection();
        }

        private string GetViewTypeName(ViewType viewType)
        {
            switch (viewType)
            {
                case ViewType.FloorPlan: return "Floor Plan";
                case ViewType.CeilingPlan: return "Ceiling Plan";
                case ViewType.Elevation: return "Elevation";
                case ViewType.ThreeD: return "3D View";
                case ViewType.Schedule: return "Schedule";
                case ViewType.Section: return "Section";
                case ViewType.Detail: return "Detail";
                case ViewType.DraftingView: return "Drafting";
                case ViewType.AreaPlan: return "Area Plan";
                case ViewType.EngineeringPlan: return "Engineering Plan";
                default: return viewType.ToString();
            }
        }

        private void ViewSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = ViewSearchBox.Text?.Trim() ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredViews = new ObservableCollection<ViewItem>(_allViews.OrderBy(v => v.Name));
            }
            else
            {
                _filteredViews = new ObservableCollection<ViewItem>(
                    _allViews.Where(v => v.Name.IndexOf(
                            searchText,
                            StringComparison.CurrentCultureIgnoreCase) >= 0)
                             .OrderBy(v => v.Name));
            }

            RestoreFilteredViewSelection();
        }

        private void ViewsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingViewList)
                return;

            foreach (ViewItem addedItem in e.AddedItems.OfType<ViewItem>())
                _selectedViewIds.Add(addedItem.ViewId.GetIdValue());

            foreach (ViewItem removedItem in e.RemovedItems.OfType<ViewItem>())
                _selectedViewIds.Remove(removedItem.ViewId.GetIdValue());

            UpdateSelectionSummary();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRequestPending)
                return;

            List<ElementId> selectedViewIds = _allViews
                .Where(viewItem => _selectedViewIds.Contains(viewItem.ViewId.GetIdValue()))
                .Select(viewItem => viewItem.ViewId)
                .ToList();

            if (selectedViewIds.Count == 0)
                return;

            ApplyRequested?.Invoke(
                this,
                new ApplyToViewsRequestedEventArgs(
                    _document,
                    _currentView.Id,
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

        public void SetRequestState(bool isPending, string message, bool isError = false)
        {
            _isRequestPending = isPending;
            ResultStatusBorder.Visibility = string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            ResultStatusText.Text = isError && !string.IsNullOrWhiteSpace(message)
                ? $"Error: {message}"
                : message;
            string brushKey = isError ? "ErrorBrush" : "ForegroundBrush";
            ResultStatusText.Foreground = (System.Windows.Media.Brush)(TryFindResource(brushKey)
                ?? System.Windows.Media.Brushes.Black);
            ResultStatusDismissButton.Visibility = !isPending && !string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            UpdateSelectionSummary();
        }

        private void ResultStatusDismissButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRequestPending)
                ResultStatusBorder.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void UpdateSelectionSummary()
        {
            int count = _selectedViewIds.Count;
            int visibleSelectedCount = _filteredViews?.Count(viewItem =>
                _selectedViewIds.Contains(viewItem.ViewId.GetIdValue())) ?? 0;

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
                foreach (ViewItem viewItem in _filteredViews)
                {
                    if (_selectedViewIds.Contains(viewItem.ViewId.GetIdValue()))
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

    public class ViewItem : INotifyPropertyChanged
    {
        public ElementId ViewId { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ApplyToViewsRequestedEventArgs : EventArgs
    {
        public ApplyToViewsRequestedEventArgs(
            Document document,
            ElementId sourceViewId,
            IReadOnlyList<ElementId> targetViewIds)
        {
            Document = document;
            SourceViewId = sourceViewId;
            TargetViewIds = targetViewIds;
        }

        public Document Document { get; }
        public ElementId SourceViewId { get; }
        public IReadOnlyList<ElementId> TargetViewIds { get; }
    }
}
