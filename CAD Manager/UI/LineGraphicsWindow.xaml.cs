using CAD_Manager.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CAD_Manager.UI
{
    public partial class LineGraphicsWindow : Window
    {
        private const string NoOverrideValue = "<No Override>";
        private const string MixedValue = "<Varies>";
        private const string NoOverrideText = "No override — use object style";
        private const string MixedText = "Mixed values — leave unchanged";
        private bool _isInitializing;
        private bool _isPending;
        private bool _colorChanged;
        private bool _patternChanged;
        private bool _weightChanged;
        private System.Drawing.Color _currentColor = System.Drawing.Color.Gray;

        public LineGraphicsWindow(Services.ThemeManager themeManager)
        {
            InitializeComponent();
            themeManager?.LoadTheme(this);
            InitializeControls();
        }

        public event EventHandler<LineGraphicsApplyRequestedEventArgs> ApplyRequested;

        public bool HasUnsavedChanges => _colorChanged || _patternChanged || _weightChanged;

        public bool TryBeginLoad(string scopeDescription)
        {
            if (_isPending)
                return false;

            if (HasUnsavedChanges)
            {
                SetRequestState(false, "Apply or close the current edits before inspecting another selection.", true);
                return false;
            }

            ScopeText.Text = scopeDescription;
            Title = $"Line Graphics — {scopeDescription}";
            SetRequestState(true, "Loading current graphics…");
            return true;
        }

        public void LoadSnapshot(LineGraphicsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                SetRequestState(false, "No graphics information was returned.", true);
                return;
            }

            _isInitializing = true;
            PatternComboBox.Items.Clear();
            AddOption(PatternComboBox, NoOverrideValue, NoOverrideText);
            foreach (string patternName in snapshot.PatternNames)
                AddOption(PatternComboBox, patternName, patternName);

            WeightComboBox.Items.Clear();
            AddOption(WeightComboBox, NoOverrideValue, NoOverrideText);
            for (int weight = 1; weight <= 16; weight++)
                AddOption(WeightComboBox, weight.ToString(), weight.ToString());

            if (snapshot.PatternVaries)
            {
                PatternComboBox.Items.Insert(0, new GraphicsOption(MixedValue, MixedText));
                PatternComboBox.SelectedIndex = 0;
            }
            else
            {
                SelectOption(PatternComboBox, snapshot.Pattern ?? NoOverrideValue);
            }

            if (snapshot.WeightVaries)
            {
                WeightComboBox.Items.Insert(0, new GraphicsOption(MixedValue, MixedText));
                WeightComboBox.SelectedIndex = 0;
            }
            else
            {
                SelectOption(
                    WeightComboBox,
                    snapshot.Weight.HasValue
                        ? snapshot.Weight.Value.ToString()
                        : NoOverrideValue);
            }

            if (snapshot.ColorVaries)
            {
                _currentColor = System.Drawing.Color.Gray;
                UpdateColorDisplayWithText(MixedText);
            }
            else if (snapshot.Color != null)
            {
                _currentColor = System.Drawing.Color.FromArgb(snapshot.Color.Red, snapshot.Color.Green, snapshot.Color.Blue);
                UpdateColorDisplay();
            }
            else
            {
                _currentColor = System.Drawing.Color.Gray;
                UpdateColorDisplayWithText(NoOverrideText);
            }

            _colorChanged = false;
            _patternChanged = false;
            _weightChanged = false;
            _isInitializing = false;
            SetRequestState(false, "Current graphics loaded.");
        }

        public void SetRequestState(bool isPending, string message, bool isError = false)
        {
            _isPending = isPending;
            PatternComboBox.IsEnabled = !isPending;
            WeightComboBox.IsEnabled = !isPending;
            ColorButton.IsEnabled = !isPending;
            ClearButton.IsEnabled = !isPending;
            ProgressBar.Visibility = isPending && Services.ThemeManager.AreAnimationsEnabled
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            StatusText.Text = isError && !string.IsNullOrWhiteSpace(message)
                ? $"Error: {message}"
                : message ?? string.Empty;
            string brushKey = isError ? "ErrorBrush" : "ForegroundBrush";
            StatusText.Foreground = (System.Windows.Media.Brush)(TryFindResource(brushKey)
                ?? System.Windows.Media.Brushes.Black);
            StatusBorder.Visibility = string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            StatusDismissButton.Visibility = !isPending && !string.IsNullOrWhiteSpace(message)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            UpdateActionState();
        }

        public void MarkApplied(bool cleared, string message)
        {
            _isInitializing = true;
            if (cleared)
            {
                SelectOption(PatternComboBox, NoOverrideValue);
                SelectOption(WeightComboBox, NoOverrideValue);
                _currentColor = System.Drawing.Color.Gray;
                UpdateColorDisplayWithText(NoOverrideText);
            }

            _colorChanged = false;
            _patternChanged = false;
            _weightChanged = false;
            _isInitializing = false;
            SetRequestState(false, message);
        }

        private void InitializeControls()
        {
            _isInitializing = true;
            AddOption(PatternComboBox, NoOverrideValue, NoOverrideText);
            AddOption(WeightComboBox, NoOverrideValue, NoOverrideText);
            for (int weight = 1; weight <= 16; weight++)
                AddOption(WeightComboBox, weight.ToString(), weight.ToString());

            PatternComboBox.SelectedIndex = 0;
            WeightComboBox.SelectedIndex = 0;
            _colorChanged = false;
            _patternChanged = false;
            _weightChanged = false;
            _isInitializing = false;
            UpdateActionState();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PatternComboBox.Focus();
            Keyboard.Focus(PatternComboBox);
        }

        private void PatternComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _patternChanged = GetSelectedValue(PatternComboBox) != MixedValue;
                UpdateActionState();
            }
        }

        private void WeightComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                _weightChanged = GetSelectedValue(WeightComboBox) != MixedValue;
                UpdateActionState();
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = _currentColor
            })
            {
                if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                _currentColor = colorDialog.Color;
                _colorChanged = true;
                UpdateColorDisplay();
                UpdateActionState();
            }
        }

        private void UpdateColorDisplay()
        {
            ColorPreview.Visibility = System.Windows.Visibility.Visible;
            ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                _currentColor.R,
                _currentColor.G,
                _currentColor.B));
            ColorText.Text = $"RGB {_currentColor.R}, {_currentColor.G}, {_currentColor.B}";
        }

        private void UpdateColorDisplayWithText(string text)
        {
            ColorPreview.Visibility = System.Windows.Visibility.Collapsed;
            ColorText.Text = text;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            RaiseApplyRequested(true);
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!HasUnsavedChanges)
            {
                SetRequestState(false, "No changes to apply.");
                return;
            }

            RaiseApplyRequested(false);
        }

        private void RaiseApplyRequested(bool clearOverrides)
        {
            if (_isPending)
                return;

            LineGraphicsColor color = _colorChanged
                ? new LineGraphicsColor(_currentColor.R, _currentColor.G, _currentColor.B)
                : null;

            string pattern = null;
            if (_patternChanged)
            {
                string selectedPattern = GetSelectedValue(PatternComboBox);
                pattern = selectedPattern == MixedValue ? null : selectedPattern;
            }

            int? weight = null;
            if (_weightChanged)
            {
                string selectedWeight = GetSelectedValue(WeightComboBox);
                if (selectedWeight == NoOverrideValue)
                    weight = -1;
                else if (selectedWeight != MixedValue && int.TryParse(selectedWeight, out int parsedWeight))
                    weight = parsedWeight;
            }

            ApplyRequested?.Invoke(this, new LineGraphicsApplyRequestedEventArgs(
                color,
                pattern,
                weight,
                clearOverrides));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void StatusDismissButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPending)
                StatusBorder.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void UpdateActionState()
        {
            ApplyButton.IsEnabled = HasUnsavedChanges && !_isPending;
            ApplyButton.Content = HasUnsavedChanges ? "_Apply Changes" : "No Changes";
            System.Windows.Automation.AutomationProperties.SetHelpText(
                ApplyButton,
                HasUnsavedChanges
                    ? "Applies the staged line graphics changes to this scope."
                    : "Choose a pattern, color, or weight before applying.");
        }

        private static void AddOption(ComboBox comboBox, string value, string displayText)
        {
            comboBox.Items.Add(new GraphicsOption(value, displayText));
        }

        private static void SelectOption(ComboBox comboBox, string value)
        {
            foreach (GraphicsOption option in comboBox.Items)
            {
                if (string.Equals(option.Value, value, StringComparison.Ordinal))
                {
                    comboBox.SelectedItem = option;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private static string GetSelectedValue(ComboBox comboBox)
        {
            GraphicsOption option = comboBox.SelectedItem as GraphicsOption;
            return option?.Value;
        }

        private sealed class GraphicsOption
        {
            public GraphicsOption(string value, string displayText)
            {
                Value = value;
                DisplayText = displayText;
            }

            public string Value { get; }
            public string DisplayText { get; }

            // Keep the selected-value presenter readable even when a custom
            // ComboBox template does not apply DisplayMemberPath to the
            // selection box content.
            public override string ToString()
            {
                return DisplayText;
            }
        }
    }

    public sealed class LineGraphicsApplyRequestedEventArgs : EventArgs
    {
        public LineGraphicsApplyRequestedEventArgs(
            LineGraphicsColor color,
            string pattern,
            int? weight,
            bool clearOverrides)
        {
            Color = color;
            Pattern = pattern;
            Weight = weight;
            ClearOverrides = clearOverrides;
        }

        public LineGraphicsColor Color { get; }
        public string Pattern { get; }
        public int? Weight { get; }
        public bool ClearOverrides { get; }
    }
}
