using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CAD_Manager.Helpers;

namespace CAD_Manager.UI
{
    public partial class LineGraphicsWindow : Window
    {
        public Autodesk.Revit.DB.Color SelectedColor { get; private set; }
        public string SelectedPattern { get; private set; }
        public int? SelectedWeight { get; private set; }
        public bool ClearOverridesRequested { get; private set; }

        // Dirty tracking
        private bool _colorChanged = false;
        private bool _patternChanged = false;
        private bool _weightChanged = false;

        private System.Drawing.Color _currentColor = System.Drawing.Color.Red;
        private System.Drawing.Color _initialColor;
        private string _initialPattern;
        private int? _initialWeight;
        
        private readonly List<DWGNode> _selectedNodes;
        private readonly UIDocument _uiDoc;
        private readonly bool _isLayerOverride;

        public LineGraphicsWindow(Services.ThemeManager themeManager, List<DWGNode> selectedNodes, UIDocument uiDoc, bool isLayerOverride)
        {
            InitializeComponent();
            
            _selectedNodes = selectedNodes;
            _uiDoc = uiDoc;
            _isLayerOverride = isLayerOverride;
            
            // Apply theme from ThemeManager
            if (themeManager != null)
            {
                themeManager.LoadTheme(this);
            }
            
            InitializeControls();
            
            // Ensure LoadCurrentState is called after the template is fully loaded
            this.Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadCurrentState();
                    AttachChangeHandlers(); // Attach after initial values are set
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        private void LoadThemeFromOwner()
        {
            // This method is no longer needed but kept for compatibility
        }

        private void InitializeControls()
        {
            // Initialize Pattern ComboBox - only add <No Override>, patterns will be loaded from project
            PatternComboBox.Items.Add("<No Override>");

            // Initialize Weight ComboBox
            WeightComboBox.Items.Add("<No Override>");
            for (int i = 1; i <= 16; i++)
            {
                WeightComboBox.Items.Add(i.ToString());
            }
            // Don't set default selection - will be set by LoadCurrentState

            // Set initial color
            SelectedColor = new Autodesk.Revit.DB.Color(_currentColor.R, _currentColor.G, _currentColor.B);
        }

        private void LoadCurrentState()
        {
            if (_selectedNodes == null || _selectedNodes.Count == 0 || _uiDoc == null)
                return;

            Document doc = _uiDoc.Document;
            View activeView = doc.ActiveView;

            Autodesk.Revit.DB.Color firstColor = null;
            int? firstWeight = null;
            ElementId firstPatternId = null;
            bool colorVaries = false;
            bool weightVaries = false;
            bool patternVaries = false;
            bool hasColorOverride = false;
            bool hasWeightOverride = false;
            bool hasPatternOverride = false;
            int itemsChecked = 0;

            foreach (var dwgNode in _selectedNodes)
            {
                if (dwgNode.ElementId == null) continue;

                ImportInstance importInstance = doc.GetElement(dwgNode.ElementId) as ImportInstance;
                if (importInstance == null || importInstance.Category == null) continue;

                OverrideGraphicSettings overrides;
                
                if (_isLayerOverride && dwgNode.Layers != null && dwgNode.Layers.Count > 0)
                {
                    // Check layer overrides
                    foreach (var layer in dwgNode.Layers)
                    {
                        var layerCategory = FindLayerCategory(importInstance, layer.Name);
                        if (layerCategory != null)
                        {
                            overrides = activeView.GetCategoryOverrides(layerCategory.Id);
                            CheckOverride(overrides, ref firstColor, ref firstWeight, ref firstPatternId, 
                                ref colorVaries, ref weightVaries, ref patternVaries, 
                                ref hasColorOverride, ref hasWeightOverride, ref hasPatternOverride, ref itemsChecked);
                        }
                    }
                }
                else
                {
                    // Check DWG-level overrides
                    overrides = activeView.GetCategoryOverrides(importInstance.Category.Id);
                    CheckOverride(overrides, ref firstColor, ref firstWeight, ref firstPatternId, 
                        ref colorVaries, ref weightVaries, ref patternVaries, 
                        ref hasColorOverride, ref hasWeightOverride, ref hasPatternOverride, ref itemsChecked);
                }
            }

            // Load line patterns from project
            LoadLinePatterns(doc);

            // Update UI based on detected state
            if (colorVaries)
            {
                _currentColor = System.Drawing.Color.Gray;
                UpdateColorDisplayWithText("<Varies>");
            }
            else if (hasColorOverride && firstColor != null)
            {
                _currentColor = System.Drawing.Color.FromArgb(firstColor.Red, firstColor.Green, firstColor.Blue);
                SelectedColor = firstColor;
                UpdateColorDisplay();
            }
            else
            {
                _currentColor = System.Drawing.Color.Gray;
                UpdateColorDisplayWithText("<No Override>");
            }

            // Set Pattern ComboBox
            if (patternVaries)
            {
                PatternComboBox.Items.Insert(0, "<Varies>");
                PatternComboBox.SelectedIndex = 0;
            }
            else if (firstPatternId != null && firstPatternId != ElementId.InvalidElementId)
            {
                var patternElement = doc.GetElement(firstPatternId) as LinePatternElement;
                if (patternElement != null)
                {
                    PatternComboBox.SelectedItem = patternElement.Name;
                }
                else
                {
                    PatternComboBox.SelectedIndex = 0; // <No Override>
                }
            }
            else
            {
                PatternComboBox.SelectedIndex = 0; // <No Override>
            }

            // Set Weight ComboBox
            if (weightVaries)
            {
                // Check if <Varies> is already in the list
                if (!WeightComboBox.Items.Contains("<Varies>"))
                {
                    WeightComboBox.Items.Insert(0, "<Varies>");
                }
                WeightComboBox.SelectedIndex = 0;
            }
            else if (firstWeight.HasValue && firstWeight.Value > 0)
            {
                WeightComboBox.SelectedItem = firstWeight.Value.ToString();
            }
            else
            {
                // Already has <No Override> at index 0, just select it
                WeightComboBox.SelectedIndex = 0;
            }

            // Store initial values for dirty tracking
            _initialColor = _currentColor;
            _initialPattern = PatternComboBox.SelectedItem?.ToString();
            _initialWeight = WeightComboBox.SelectedItem?.ToString() == "<No Override>" || WeightComboBox.SelectedItem?.ToString() == "<Varies>" 
                ? null 
                : (int?)int.Parse(WeightComboBox.SelectedItem?.ToString() ?? "0");
        }

        private void AttachChangeHandlers()
        {
            // Attach change handlers AFTER initial values are set
            PatternComboBox.SelectionChanged += (s, e) => _patternChanged = true;
            WeightComboBox.SelectionChanged += (s, e) => _weightChanged = true;
        }

        private void LoadLinePatterns(Document doc)
        {
            // Clear existing items except <No Override>
            while (PatternComboBox.Items.Count > 1)
            {
                PatternComboBox.Items.RemoveAt(1);
            }

            // Get all line pattern elements from the project
            var linePatterns = new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .OrderBy(lp => lp.Name);

            foreach (var pattern in linePatterns)
            {
                PatternComboBox.Items.Add(pattern.Name);
            }
        }

        private void CheckOverride(OverrideGraphicSettings overrides, ref Autodesk.Revit.DB.Color firstColor, ref int? firstWeight, ref ElementId firstPatternId, ref bool colorVaries, ref bool weightVaries, ref bool patternVaries, ref bool hasColorOverride, ref bool hasWeightOverride, ref bool hasPatternOverride, ref int itemsChecked)
        {
            var color = overrides.ProjectionLineColor;
            var weight = overrides.ProjectionLineWeight;
            var patternId = overrides.ProjectionLinePatternId;

            itemsChecked++;

            // --- COLOR CHECK ---
            // Determine effective color (null if invalid/no override)
            Autodesk.Revit.DB.Color effectiveColor = null;
            if (color != null && color.IsValid && (color.Red != 0 || color.Green != 0 || color.Blue != 0))
            {
                effectiveColor = color;
            }

            if (itemsChecked == 1)
            {
                firstColor = effectiveColor;
                if (firstColor != null) hasColorOverride = true; 
            }
            else if (!colorVaries)
            {
                // Compare effective colors
                // If one is null and other is not -> varies
                // If both are not null but different RGB -> varies
                if ((firstColor == null && effectiveColor != null) ||
                    (firstColor != null && effectiveColor == null) ||
                    (firstColor != null && effectiveColor != null && !ColorsEqual(firstColor, effectiveColor)))
                {
                    colorVaries = true;
                }
            }

            // --- PATTERN CHECK ---
            // Determine effective pattern (null if invalid/no override)
            ElementId effectivePatternId = null;
            if (patternId != null && patternId != ElementId.InvalidElementId)
            {
                effectivePatternId = patternId;
            }

            if (itemsChecked == 1)
            {
                firstPatternId = effectivePatternId;
                if (firstPatternId != null) hasPatternOverride = true;
            }
            else if (!patternVaries)
            {
                if ((firstPatternId == null && effectivePatternId != null) ||
                    (firstPatternId != null && effectivePatternId == null) ||
                    (firstPatternId != null && effectivePatternId != null && firstPatternId.GetIdValue() != effectivePatternId.GetIdValue()))
                {
                    patternVaries = true;
                }
            }

            // --- WEIGHT CHECK ---
            // Determine effective weight (null if <= 0)
            int? effectiveWeight = null;
            if (weight > 0)
            {
                effectiveWeight = weight;
            }

            if (itemsChecked == 1)
            {
                firstWeight = effectiveWeight;
                if (firstWeight.HasValue) hasWeightOverride = true;
            }
            else if (!weightVaries)
            {
                if (firstWeight != effectiveWeight)
                {
                    weightVaries = true;
                }
            }
        }

        private bool ColorsEqual(Autodesk.Revit.DB.Color c1, Autodesk.Revit.DB.Color c2)
        {
            return c1.Red == c2.Red && c1.Green == c2.Green && c1.Blue == c2.Blue;
        }

        private Category FindLayerCategory(ImportInstance importInstance, string layerName)
        {
            var key = layerName?.Normalize(System.Text.NormalizationForm.FormKC);
            return importInstance.Category?.SubCategories?
                .Cast<Category>()
                .FirstOrDefault(subCat =>
                    (subCat.Name?.Normalize(System.Text.NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase));
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            using (var colorDialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = _currentColor
            })
            {
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _currentColor = colorDialog.Color;
                    SelectedColor = new Autodesk.Revit.DB.Color(_currentColor.R, _currentColor.G, _currentColor.B);
                    UpdateColorDisplay();
                    _colorChanged = true; // Mark as changed
                }
            }
        }

        private void UpdateColorDisplay()
        {
            // Find the template elements
            if (ColorButton.Template != null)
            {
                var colorPreview = ColorButton.Template.FindName("ColorPreview", ColorButton) as Border;
                var colorText = ColorButton.Template.FindName("ColorText", ColorButton) as TextBlock;

                if (colorPreview != null)
                {
                    // Show the color preview box
                    colorPreview.Visibility = System.Windows.Visibility.Visible;
                    colorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                        _currentColor.R, _currentColor.G, _currentColor.B));
                }

                if (colorText != null)
                {
                    // Reset text to left-aligned with margin
                    colorText.Text = _currentColor.Name;
                    colorText.HorizontalAlignment = HorizontalAlignment.Left;
                    colorText.Margin = new Thickness(35, 0, 0, 0);
                }
            }
        }

        private void UpdateColorDisplayWithText(string text)
        {
            if (ColorButton.Template != null)
            {
                var colorPreview = ColorButton.Template.FindName("ColorPreview", ColorButton) as Border;
                var colorText = ColorButton.Template.FindName("ColorText", ColorButton) as TextBlock;

                if (colorPreview != null)
                {
                    // Hide the color preview box for special states
                    colorPreview.Visibility = System.Windows.Visibility.Collapsed;
                }

                if (colorText != null)
                {
                    // Center the text and remove margin
                    colorText.Text = text;
                    colorText.HorizontalAlignment = HorizontalAlignment.Center;
                    colorText.Margin = new Thickness(0);
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearOverridesRequested = true;
            DialogResult = true;
            Close();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            // Only set values that were actually changed
            if (_colorChanged)
            {
                SelectedColor = new Autodesk.Revit.DB.Color(_currentColor.R, _currentColor.G, _currentColor.B);
            }
            else
            {
                SelectedColor = null; // Don't apply color if not changed
            }

            if (_patternChanged)
            {
                var selectedPattern = PatternComboBox.SelectedItem?.ToString();
                // If <Varies>, treat as no change (null). If <No Override>, treat as explicit clear.
                SelectedPattern = (selectedPattern == "<Varies>") ? null : selectedPattern;
            }
            else
            {
                SelectedPattern = null; // Don't apply pattern if not changed
            }

            if (_weightChanged)
            {
                var selectedWeight = WeightComboBox.SelectedItem?.ToString();
                if (selectedWeight == "<Varies>")
                {
                    SelectedWeight = null; // Treat as no change
                }
                else if (selectedWeight == "<No Override>")
                {
                    SelectedWeight = -1; // Explicit clear signal
                }
                else
                {
                    SelectedWeight = selectedWeight != null ? (int?)int.Parse(selectedWeight) : null;
                }
            }
            else
            {
                SelectedWeight = null; // Don't apply weight if not changed
            }

            ClearOverridesRequested = false;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
