using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CAD_Manager.UI
{
    public partial class ApplyToViewsWindow : Window
    {
        private readonly Document _document;
        private readonly View _currentView;
        private ObservableCollection<ViewItem> _allViews;
        private ObservableCollection<ViewItem> _filteredViews;

        public List<View> SelectedViews { get; private set; }

        public ApplyToViewsWindow(Document document, View currentView)
        {
            InitializeComponent();
            _document = document;
            _currentView = currentView;
            
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
                    View = view,
                    Name = view.Name,
                    ViewType = GetViewTypeName(view.ViewType)
                });
            }

            _filteredViews = new ObservableCollection<ViewItem>(_allViews.OrderBy(v => v.Name));
            ViewsDataGrid.ItemsSource = _filteredViews;
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
            string searchText = ViewSearchBox.Text.ToLower();
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredViews = new ObservableCollection<ViewItem>(_allViews.OrderBy(v => v.Name));
            }
            else
            {
                _filteredViews = new ObservableCollection<ViewItem>(
                    _allViews.Where(v => v.Name.ToLower().Contains(searchText))
                             .OrderBy(v => v.Name));
            }
            
            ViewsDataGrid.ItemsSource = _filteredViews;
        }

        private void ViewsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyButton.IsEnabled = ViewsDataGrid.SelectedItems.Count > 0;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedViews = ViewsDataGrid.SelectedItems.Cast<ViewItem>().Select(vi => vi.View).ToList();
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }

    public class ViewItem : INotifyPropertyChanged
    {
        public View View { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
