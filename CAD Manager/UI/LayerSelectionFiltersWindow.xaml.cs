using CAD_Manager.Core;
using CAD_Manager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CAD_Manager.UI
{
    public partial class LayerSelectionFiltersWindow : Window
    {
        public LayerSelectionFiltersWindow(
            ThemeManager themeManager,
            IEnumerable<LayerSelectionFilterRule> rules)
        {
            InitializeComponent();
            themeManager?.LoadTheme(this);

            MatchTypes = Enum.GetValues(typeof(LayerNameMatchType))
                .Cast<LayerNameMatchType>()
                .Select(value => new LayerNameMatchTypeOption(value))
                .ToList()
                .AsReadOnly();
            Rules = new ObservableCollection<LayerSelectionFilterRule>(
                (rules ?? Enumerable.Empty<LayerSelectionFilterRule>()).Select(CloneRule));
            DataContext = this;
        }

        public ObservableCollection<LayerSelectionFilterRule> Rules { get; }

        public IReadOnlyList<LayerNameMatchTypeOption> MatchTypes { get; }

        public event EventHandler<LayerSelectionFiltersSavedEventArgs> FiltersSaved;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Rules.Count == 0)
                AddRule();

            RulesDataGrid.Focus();
            Keyboard.Focus(RulesDataGrid);
        }

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            AddRule();
        }

        private void AddRule()
        {
            LayerSelectionFilterRule rule = new LayerSelectionFilterRule();
            Rules.Add(rule);
            RulesDataGrid.SelectedItem = rule;
            RulesDataGrid.ScrollIntoView(rule);
            RulesDataGrid.UpdateLayout();
            RulesDataGrid.Dispatcher.BeginInvoke(new Action(() =>
            {
                DataGridRow row = RulesDataGrid.ItemContainerGenerator.ContainerFromItem(rule) as DataGridRow;
                TextBox patternEditor = FindVisualChild<TextBox>(row);
                if (patternEditor == null)
                    return;

                patternEditor.Focus();
                Keyboard.Focus(patternEditor);
                patternEditor.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is LayerSelectionFilterRule rule)
                Rules.Remove(rule);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            RulesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            RulesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<LayerSelectionFilterRule> savedRules = Rules
                .Where(rule => rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
                .Select(CloneRule)
                .ToList();

            try
            {
                FiltersSaved?.Invoke(this, new LayerSelectionFiltersSavedEventArgs(savedRules));
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Filters could not be saved: {ex.Message}";
                StatusText.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static LayerSelectionFilterRule CloneRule(LayerSelectionFilterRule rule)
        {
            return new LayerSelectionFilterRule
            {
                IsEnabled = rule.IsEnabled,
                MatchType = rule.MatchType,
                Pattern = rule.Pattern
            };
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    return match;

                T descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        public sealed class LayerNameMatchTypeOption
        {
            public LayerNameMatchTypeOption(LayerNameMatchType value)
            {
                Value = value;
                DisplayName = new LayerSelectionFilterRule
                {
                    MatchType = value
                }.MatchTypeDisplayName;
            }

            public LayerNameMatchType Value { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }

    public sealed class LayerSelectionFiltersSavedEventArgs : EventArgs
    {
        public LayerSelectionFiltersSavedEventArgs(
            IReadOnlyList<LayerSelectionFilterRule> filters)
        {
            Filters = filters ?? new List<LayerSelectionFilterRule>();
        }

        public IReadOnlyList<LayerSelectionFilterRule> Filters { get; }
    }
}
