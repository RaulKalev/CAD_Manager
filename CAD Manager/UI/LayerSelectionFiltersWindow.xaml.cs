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
        private readonly Func<IReadOnlyList<DwgLayerVisibility>> _activeViewLayers;

        public LayerSelectionFiltersWindow(
            ThemeManager themeManager,
            IEnumerable<LayerSelectionFilterRule> rules,
            Func<IReadOnlyList<DwgLayerVisibility>> activeViewLayers = null)
        {
            InitializeComponent();
            themeManager?.LoadTheme(this);

            _activeViewLayers = activeViewLayers;
            if (_activeViewLayers == null)
                ImportFromViewButton.Visibility = System.Windows.Visibility.Collapsed;

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

        private void ImportFromViewButton_Click(object sender, RoutedEventArgs e)
        {
            RulesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            RulesDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            IReadOnlyList<DwgLayerVisibility> layers;
            try
            {
                layers = _activeViewLayers();
            }
            catch (Exception ex)
            {
                ShowStatus($"Hidden layers could not be read: {ex.Message}", true);
                return;
            }

            if (layers == null || !layers.Any(dwg => dwg.HiddenLayers.Count > 0))
            {
                ShowStatus("No DWG layers are hidden in the active view.", false);
                return;
            }

            IReadOnlyList<LayerSelectionFilterRule> proposals =
                LayerFilterRuleSuggester.Suggest(layers, Rules);
            if (proposals.Count == 0)
            {
                ShowStatus("Every hidden layer already matches a rule in this list.", false);
                return;
            }

            // Drop the blank starter row so proposals are not mixed with an empty rule.
            foreach (LayerSelectionFilterRule blankRule in Rules
                         .Where(rule => string.IsNullOrWhiteSpace(rule.Pattern))
                         .ToList())
            {
                Rules.Remove(blankRule);
            }

            foreach (LayerSelectionFilterRule proposal in proposals)
                Rules.Add(proposal);

            RulesDataGrid.SelectedItem = proposals[0];
            RulesDataGrid.ScrollIntoView(proposals[0]);

            string noun = proposals.Count == 1 ? "rule" : "rules";
            ShowStatus(
                $"Added {proposals.Count} proposed {noun}. Review them, then choose Save filters.",
                false);
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                isError ? "ErrorBrush" : "SecondaryForegroundBrush");
            StatusText.Visibility = System.Windows.Visibility.Visible;
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
                ShowStatus($"Filters could not be saved: {ex.Message}", true);
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
