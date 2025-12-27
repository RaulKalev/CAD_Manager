using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using CAD_Manager.Models;
using CAD_Manager.Services;
using CAD_Manager.Handlers;
using CAD_Manager.Helpers;
using CAD_Manager.UI;
using CAD_Manager.ViewModels;
using System.Windows.Media;

namespace CAD_Manager
{
    public partial class CADManagerWindow : Window
    {
        private object _lastSelectedNode = null;
        private bool IsShiftPressed => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        private bool IsCtrlPressed => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        
        public List<DWGNode> DWGNodes { get; set; }
        public List<DWGNode> FilteredDWGNodes { get; set; }
        private readonly UIDocument _uiDoc;
        private readonly VisibilityToggler _visibilityToggler;
        private readonly ExternalEvent _externalEvent;
        private readonly HalftoneHandler _halftoneHandler;
        private readonly ExternalEvent _halftoneEvent;
        private readonly ApplyToViewsHandler _applyToViewsHandler;
        private readonly ExternalEvent _applyToViewsEvent;
        private readonly CommandButtons _commandButtons;
        private readonly TreeViewControls _treeViewControls;
        private readonly WindowResizer _windowResizer;
        private readonly ColorOverrideHandler _colorOverrideHandler;
        private readonly ExternalEvent _colorOverrideEvent;
        private readonly ThemeManager _themeManager;

        public CADManagerWindow(List<DWGNode> dwgNodes, UIDocument uiDoc)
        {
            InitializeComponent();

            DWGNodes = dwgNodes;
            _uiDoc = uiDoc;
            BuildLayerParentLookup();
            Topmost = true;

            _visibilityToggler = new VisibilityToggler
            {
                DWGNodes = DWGNodes,
                Document = _uiDoc.Document,
                CurrentView = _uiDoc.Document.ActiveView
            };

            _externalEvent = ExternalEvent.Create(_visibilityToggler);
            _treeViewControls = new TreeViewControls(_externalEvent, DWGNodes);

            _colorOverrideHandler = new ColorOverrideHandler();
            _colorOverrideEvent = ExternalEvent.Create(_colorOverrideHandler);

            _halftoneHandler = new HalftoneHandler
            {
                Document = _uiDoc.Document,
                CurrentView = _uiDoc.Document.ActiveView
            };
            _halftoneEvent = ExternalEvent.Create(_halftoneHandler);

            _applyToViewsHandler = new ApplyToViewsHandler
            {
                Document = _uiDoc.Document
            };
            _applyToViewsEvent = ExternalEvent.Create(_applyToViewsHandler);

            _themeManager = new ThemeManager(this);

            _treeViewControls.SortDWGs(DWGNodes);

            FilteredDWGNodes = DWGNodes;

            _commandButtons = new CommandButtons(_uiDoc, _externalEvent, _visibilityToggler, RefreshTreeView, this);

            DWGTreeView.ItemsSource = FilteredDWGNodes;
            DWGTreeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
            DWGTreeView.PreviewKeyDown += DWGTreeView_PreviewKeyDown;
            this.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);

            this.Closed += Window1_Closed;

            DWGTreeView.Loaded += (s, e) => _treeViewControls.ExpandAllNodes(DWGNodes);

            _windowResizer = new WindowResizer(this);

            // Global mouse events for resizing
            this.MouseMove += Window_MouseMove;
            this.MouseLeftButtonUp += Window_MouseLeftButtonUp;

            if (Application.ResourceAssembly == null)
            {
                Application.ResourceAssembly = Assembly.GetExecutingAssembly();
            }

            _themeManager.LoadThemeState();
            ThemeToggleButton.IsChecked = _themeManager.IsDarkMode;
            _themeManager.LoadTheme();

            // Set initial icon state
            if (ThemeToggleButton.Template.FindName("ThemeToggleIcon", ThemeToggleButton) is MaterialDesignThemes.Wpf.PackIcon themeIcon)
            {
                themeIcon.Kind = _themeManager.IsDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }

            this.Focusable = true;
            this.Focus();
            
            if (_uiDoc.Document.IsFamilyDocument)
            {
                DisableFileButtons();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
            Keyboard.Focus(this);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ClearTreeViewSelection();
                SearchBox.Text = string.Empty;
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _themeManager.IsDarkMode = ThemeToggleButton.IsChecked == true;
            _themeManager.LoadTheme();

            if (ThemeToggleButton.Template.FindName("ThemeToggleIcon", ThemeToggleButton) is MaterialDesignThemes.Wpf.PackIcon themeIcon)
            {
                themeIcon.Kind = _themeManager.IsDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }
        }

        private void DisableFileButtons()
        {
            SaveButton.IsEnabled = false;
            LoadButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            ApplyToViewsButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;

            SaveButton.Opacity = 0.5;
            LoadButton.Opacity = 0.5;
            BrowseButton.Opacity = 0.5;
            ApplyToViewsButton.Opacity = 0.5;
            RefreshButton.Opacity = 0.5;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Search.HandleSearchBoxTextChanged(SearchBox, DWGNodes, filteredNodes =>
            {
                FilteredDWGNodes = filteredNodes;
                _treeViewControls.RefreshTreeView(DWGTreeView, FilteredDWGNodes);
            });
            ClearTreeViewSelection();
        }

        private void Window1_Closed(object sender, System.EventArgs e)
        {
            _themeManager.SaveThemeState();

            DWGTreeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
            this.MouseMove -= Window_MouseMove;
            this.MouseLeftButtonUp -= Window_MouseLeftButtonUp;
            DWGTreeView.Loaded -= (s, args) => _treeViewControls.ExpandAllNodes(DWGNodes);

            _externalEvent?.Dispose();
            _colorOverrideEvent?.Dispose();
            _halftoneEvent?.Dispose();

            _visibilityToggler.DWGNodes = null;
            _visibilityToggler.Document = null;
            _visibilityToggler.CurrentView = null;

            DWGNodes?.Clear();
            FilteredDWGNodes?.Clear();
            DWGTreeView.ItemsSource = null;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Search.ClearSearchBox(SearchBox);
        
        private void CheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                _treeViewControls.HandleCheckBoxToggled(checkBox, DWGTreeView);
                
                if (checkBox.DataContext is DWGNode dwgNode)
                {
                    SyncFilteredNodeToOriginal(dwgNode);
                }
            }
        }

        private void Halftone_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton toggleButton && toggleButton.DataContext is DWGNode clickedNode)
            {
                // The IsHalftone property of the clickedNode is already updated by binding (TwoWay)
                bool newState = clickedNode.IsHalftone;
                var nodesToUpdate = new List<DWGNode>();

                // Check if the clicked node is part of the current selection
                if (clickedNode.IsSelected)
                {
                    // Apply to all selected DWG nodes
                    var selectedNodes = DWGNodes.Where(n => n.IsSelected).ToList();
                    foreach (var node in selectedNodes)
                    {
                        if (node != clickedNode)
                        {
                            node.IsHalftone = newState;
                        }
                    }
                    nodesToUpdate.AddRange(selectedNodes);
                }
                else
                {
                    // Apply only to the clicked node
                    nodesToUpdate.Add(clickedNode);
                }
                
                _halftoneHandler.DWGNodes = nodesToUpdate;
                _halftoneEvent.Raise();
            }
        }
        
        private void SyncFilteredNodeToOriginal(DWGNode filteredNode)
        {
            if (filteredNode == null || DWGNodes == null) return;
            
            // Find the original node by ID
            // Assuming ElementId is a unique identifier for DWG imports
            var original = DWGNodes.FirstOrDefault(n => n.ElementId == filteredNode.ElementId);
            
            if (original != null && !ReferenceEquals(original, filteredNode))
            {
                original.IsChecked = filteredNode.IsChecked;
                original.IsHalftone = filteredNode.IsHalftone;
                original.LineColor = filteredNode.LineColor;
                original.LinePattern = filteredNode.LinePattern;
                original.LineWeight = filteredNode.LineWeight;
                original.IsSelected = filteredNode.IsSelected;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = System.Windows.WindowState.Minimized;

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e) => Search.HandleSearchBoxGotFocus(SearchBox);
        private void SearchBox_LostFocus(object sender, RoutedEventArgs e) => Search.HandleSearchBoxLostFocus(SearchBox);
        
        private void RefreshTreeView()
        {
            _treeViewControls.RefreshTreeView(DWGTreeView, FilteredDWGNodes);
            BuildLayerParentLookup();
        }

        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE;
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE;
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNS;
        private void Edge_MouseLeave(object sender, MouseEventArgs e) => this.Cursor = Cursors.Arrow;
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNESW;
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNWSE;
        
        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>_windowResizer.StartResizing(e, ResizeDirection.BottomRight);
        
        private void LoadButton_Click(object sender, RoutedEventArgs e) => _commandButtons.LoadButton_Click(sender, e);
        private void SaveButton_Click(object sender, RoutedEventArgs e) => _commandButtons.SaveButton_Click(sender, e);
        private void BrowseButton_Click(object sender, RoutedEventArgs e) => _commandButtons.BrowseButton_Click(sender, e);

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _commandButtons.RefreshButton_Click(sender, e);

        private void ApplyToViewsButton_Click(object sender, RoutedEventArgs e)
        {
            var applyWindow = new UI.ApplyToViewsWindow(_uiDoc.Document, _uiDoc.Document.ActiveView);
            applyWindow.Owner = this;
            
            if (applyWindow.ShowDialog() == true && applyWindow.SelectedViews != null && applyWindow.SelectedViews.Count > 0)
            {
                // Set up the handler with the selected views
                _applyToViewsHandler.SourceView = _uiDoc.Document.ActiveView;
                _applyToViewsHandler.TargetViews = applyWindow.SelectedViews;
                _applyToViewsHandler.OnComplete = (message) =>
                {
                    UniversalPopupWindow.Show(message, "Apply to Views", MessageBoxButton.OK, MessageBoxImage.Information, this);
                };
                _applyToViewsHandler.OnError = (message) =>
                {
                    UniversalPopupWindow.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                };
                
                // Raise the external event
                _applyToViewsEvent.Raise();
            }
        }

        private void TreeViewItem_EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is object node)
            {
                    if (node is DWGNode clickedDwgNode && clickedDwgNode.ElementId != null)
                    {
                        var nodesToUpdate = new List<DWGNode>();

                        // Check if the clicked node is part of the current selection
                        if (clickedDwgNode.IsSelected)
                        {
                            // Apply to all selected DWG nodes
                            nodesToUpdate.AddRange(DWGNodes.Where(n => n.IsSelected && n.ElementId != null));
                        }
                        else
                        {
                            // Apply only to the clicked node
                            nodesToUpdate.Add(clickedDwgNode);
                        }

                        // Open window with current state
                        var lineGraphicsWindow = new UI.LineGraphicsWindow(_themeManager, nodesToUpdate, _uiDoc, false)
                        {
                            Owner = this
                        };

                        if (lineGraphicsWindow.ShowDialog() != true)
                            return;

                        _colorOverrideHandler.DWGNodes = nodesToUpdate;
                        _colorOverrideHandler.OverrideColor = lineGraphicsWindow.SelectedColor;
                        _colorOverrideHandler.LinePattern = lineGraphicsWindow.SelectedPattern;
                        _colorOverrideHandler.LineWeight = lineGraphicsWindow.SelectedWeight;
                        _colorOverrideHandler.ClearOverrides = lineGraphicsWindow.ClearOverridesRequested;
                        _colorOverrideHandler.IsLayerOverride = false;
                        _colorOverrideEvent.Raise();

                        // Sync local model to prevent reversion on subsequent visibility toggles
                        foreach(var dwgNode in nodesToUpdate) 
                        {
                            if (lineGraphicsWindow.ClearOverridesRequested)
                            {
                                dwgNode.LineColor = null;
                                dwgNode.LinePattern = null;
                                dwgNode.LineWeight = null;
                            }
                            else
                            {
                                if (lineGraphicsWindow.SelectedColor != null)
                                {
                                    dwgNode.LineColor = $"#{lineGraphicsWindow.SelectedColor.Red:X2}{lineGraphicsWindow.SelectedColor.Green:X2}{lineGraphicsWindow.SelectedColor.Blue:X2}";
                                }
                                if (lineGraphicsWindow.SelectedPattern != null)
                                {
                                    dwgNode.LinePattern = lineGraphicsWindow.SelectedPattern;
                                }
                                if (lineGraphicsWindow.SelectedWeight.HasValue)
                                {
                                     if (lineGraphicsWindow.SelectedWeight.Value == -1) 
                                         dwgNode.LineWeight = null;
                                     else 
                                         dwgNode.LineWeight = lineGraphicsWindow.SelectedWeight.Value;
                                }
                            }
                            SyncFilteredNodeToOriginal(dwgNode);
                        }
                    }
                    else if (node is LayerNode clickedLayerNode)
                    {
                        var layersToUpdate = new List<LayerNode>();

                        // Check if the clicked layer is part of the current selection
                        if (clickedLayerNode.IsSelected)
                        {
                            // Apply to all selected Layer nodes
                            layersToUpdate.AddRange(DWGNodes.SelectMany(dwg => dwg.Layers).Where(l => l.IsSelected));
                        }
                        else
                        {
                            // Apply only to the clicked layer
                            layersToUpdate.Add(clickedLayerNode);
                        }

                        // Group layers by their parent DWG
                        var layersByDwg = new Dictionary<DWGNode, List<LayerNode>>();
                        foreach (var layer in layersToUpdate)
                        {
                            var parentDWGNode = FindParentDWGNode(layer);
                            if (parentDWGNode != null && parentDWGNode.ElementId != null)
                            {
                                if (!layersByDwg.ContainsKey(parentDWGNode))
                                {
                                    layersByDwg[parentDWGNode] = new List<LayerNode>();
                                }
                                layersByDwg[parentDWGNode].Add(layer);
                            }
                        }

                        // Create DWGNode wrappers for each parent with its selected layers
                        var dwgNodesToUpdate = layersByDwg.Select(kvp => new DWGNode
                        {
                            Name = kvp.Key.Name,
                            ElementId = kvp.Key.ElementId,
                            Layers = kvp.Value
                        }).ToList();

                        // Open window with current state
                        var lineGraphicsWindow = new UI.LineGraphicsWindow(_themeManager, dwgNodesToUpdate, _uiDoc, true)
                        {
                            Owner = this
                        };

                        if (lineGraphicsWindow.ShowDialog() != true)
                            return;

                        if (dwgNodesToUpdate.Any())
                        {
                            _colorOverrideHandler.DWGNodes = dwgNodesToUpdate;
                            _colorOverrideHandler.OverrideColor = lineGraphicsWindow.SelectedColor;
                            _colorOverrideHandler.LinePattern = lineGraphicsWindow.SelectedPattern;
                            _colorOverrideHandler.LineWeight = lineGraphicsWindow.SelectedWeight;
                            _colorOverrideHandler.ClearOverrides = lineGraphicsWindow.ClearOverridesRequested;
                            _colorOverrideHandler.IsLayerOverride = true;
                            _colorOverrideEvent.Raise();

                            // Sync local model to prevent reversion on subsequent visibility toggles
                            // We need to update the ORIGINAL LayerNodes in layersToUpdate
                            foreach (var layer in layersToUpdate)
                            {
                                if (lineGraphicsWindow.ClearOverridesRequested)
                                {
                                    layer.LineColor = null;
                                    layer.LinePattern = null;
                                    layer.LineWeight = null;
                                }
                                else
                                {
                                    if (lineGraphicsWindow.SelectedColor != null)
                                    {
                                        layer.LineColor = $"#{lineGraphicsWindow.SelectedColor.Red:X2}{lineGraphicsWindow.SelectedColor.Green:X2}{lineGraphicsWindow.SelectedColor.Blue:X2}";
                                    }
                                    if (lineGraphicsWindow.SelectedPattern != null)
                                    {
                                        layer.LinePattern = lineGraphicsWindow.SelectedPattern;
                                    }
                                    if (lineGraphicsWindow.SelectedWeight.HasValue)
                                    {
                                         if (lineGraphicsWindow.SelectedWeight.Value == -1) 
                                             layer.LineWeight = null;
                                         else 
                                             layer.LineWeight = lineGraphicsWindow.SelectedWeight.Value;
                                    }
                                }
                            }
                        }
                        else
                        {
                            UniversalPopupWindow.Show("Layer override failed: Could not resolve parent DWG context.", "Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                        }
                    }
            }
        }

        private Dictionary<LayerNode, DWGNode> _layerToParentCache = new Dictionary<LayerNode, DWGNode>();

        private void BuildLayerParentLookup()
        {
            _layerToParentCache.Clear();
            foreach (var dwgNode in DWGNodes)
            {
                foreach (var layer in dwgNode.Layers)
                {
                    _layerToParentCache[layer] = dwgNode;
                }
            }
        }
        private DWGNode FindParentDWGNode(LayerNode layerNode)
        {
            if (_layerToParentCache.Count == 0)
                BuildLayerParentLookup();

            return _layerToParentCache.TryGetValue(layerNode, out var parent) ? parent : null;
        }


        private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var sourceElement = e.OriginalSource as DependencyObject;
            while (sourceElement != null)
            {
                if (sourceElement is CheckBox || sourceElement is System.Windows.Controls.Primitives.ToggleButton || sourceElement is System.Windows.Controls.Button)
                {
                    return;
                }
                else if (sourceElement is TreeViewItem)
                {
                    break;
                }
                sourceElement = VisualTreeHelper.GetParent(sourceElement);
            }

            var clickedItem = GetTreeViewItemUnderMouse(e);

            if (clickedItem?.DataContext is DWGNode dwgNode)
            {
                HandleTreeViewSelection(dwgNode);
                e.Handled = true;
            }
            else if (clickedItem?.DataContext is LayerNode layerNode)
            {
                HandleTreeViewSelection(layerNode);
                e.Handled = true;
            }
            else
            {
                ClearTreeViewSelection();
            }
            if (!DWGTreeView.IsKeyboardFocusWithin)
                DWGTreeView.Focus();
        }

        private void ClearTreeViewSelection()
        {
            foreach (var dwgNode in DWGNodes)
            {
                dwgNode.IsSelected = false;
                foreach (var layer in dwgNode.Layers)
                {
                    layer.IsSelected = false;
                }
            }

            _lastSelectedNode = null;
            RefreshTreeView();
        }

        private TreeViewItem GetTreeViewItemUnderMouse(MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is TreeViewItem))
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as TreeViewItem;
        }

        private void HandleTreeViewSelection(object clickedNode)
        {
            if (IsShiftPressed && _lastSelectedNode != null)
            {
                if (_lastSelectedNode is DWGNode lastDwgNode && clickedNode is DWGNode shiftDwgNode)
                {
                    SelectRange(lastDwgNode, shiftDwgNode);
                }
                else if (_lastSelectedNode is LayerNode lastLayerNode && clickedNode is LayerNode shiftLayerNode)
                {
                    SelectRangeLayers(lastLayerNode, shiftLayerNode);
                }
            }
            else if (IsCtrlPressed)
            {
                ToggleSelection(clickedNode);
            }
            else
            {
                SelectSingle(clickedNode);
            }

            if (clickedNode is DWGNode selectedDwgNode)
            {
                _lastSelectedNode = selectedDwgNode;
            }
            else if (clickedNode is LayerNode selectedLayerNode)
            {
                _lastSelectedNode = selectedLayerNode;
            }

            RefreshTreeView();
        }

        private void SelectRange(DWGNode startNode, DWGNode endNode)
        {
            bool inRange = false;
            foreach (var node in DWGNodes)
            {
                if (node == startNode || node == endNode)
                {
                    node.IsSelected = true;
                    inRange = !inRange;
                }
                if (inRange || node == startNode || node == endNode)
                {
                    node.IsSelected = true;
                }
            }
        }

        private void SelectRangeLayers(LayerNode startNode, LayerNode endNode)
        {
            bool inRange = false;
            foreach (var dwgNode in DWGNodes)
            {
                foreach (var layer in dwgNode.Layers)
                {
                    if (layer == startNode || layer == endNode)
                    {
                        layer.IsSelected = true;
                        inRange = !inRange;
                    }
                    if (inRange || layer == startNode || layer == endNode)
                    {
                        layer.IsSelected = true;
                    }
                }
            }
        }


        private void ToggleSelection(object node)
        {
            switch (node)
            {
                case DWGNode dwgNode:
                    dwgNode.IsSelected = !dwgNode.IsSelected;
                    break;
                case LayerNode layerNode:
                    layerNode.IsSelected = !layerNode.IsSelected;
                    break;
            }
        }

        private void SelectSingle(object node)
        {
            foreach (var dwgNode in DWGNodes)
            {
                dwgNode.IsSelected = false;
                foreach (var layer in dwgNode.Layers)
                {
                    layer.IsSelected = false;
                }
            }

            switch (node)
            {
                case DWGNode dwgNode:
                    dwgNode.IsSelected = true;
                    break;
                case LayerNode layerNode:
                    layerNode.IsSelected = true;
                    break;
            }
        }
        private void DWGTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var target = _lastSelectedNode
                             ?? (object)DWGNodes?.FirstOrDefault(n => n.IsSelected)
                             ?? (object)FilteredDWGNodes?.FirstOrDefault(n => n.IsSelected);

                var dwg = ResolveParentDWGFromCurrentView(target);
                if (dwg != null)
                {
                    foreach (var layer in dwg.Layers)
                        layer.IsSelected = true;

                    RefreshTreeView();
                    e.Handled = true;
                }
            }
        }

        private DWGNode ResolveParentDWGFromCurrentView(object node)
        {
            if (node is DWGNode dn)
                return dn;

            if (node is LayerNode ln)
            {
                if (FilteredDWGNodes != null)
                {
                    var fromFiltered = FilteredDWGNodes.FirstOrDefault(d => d.Layers.Contains(ln));
                    if (fromFiltered != null) return fromFiltered;
                }

                if (DWGNodes != null)
                {
                    var fromAll = DWGNodes.FirstOrDefault(d => d.Layers.Contains(ln));
                    if (fromAll != null) return fromAll;
                }

                if (!string.IsNullOrEmpty(ln.Name) && DWGNodes != null)
                {
                    var byName = DWGNodes.FirstOrDefault(d => d.Layers.Any(x => string.Equals(x.Name, ln.Name, StringComparison.CurrentCultureIgnoreCase)));
                    if (byName != null) return byName;
                }
            }

            return null;
        }
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!DWGTreeView.IsKeyboardFocusWithin)
                return;

            if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var target = _lastSelectedNode
                             ?? (object)DWGNodes?.FirstOrDefault(n => n.IsSelected)
                             ?? (object)FilteredDWGNodes?.FirstOrDefault(n => n.IsSelected);

                var dwg = ResolveParentDWGFromCurrentView(target);
                if (dwg != null)
                {
                    foreach (var layer in dwg.Layers)
                        layer.IsSelected = true;

                    RefreshTreeView();
                    e.Handled = true;
                }
            }
        }

        private TreeViewItem FindTreeViewItem(ItemsControl parent, object node)
        {
            if (parent == null) return null;

            foreach (var item in parent.Items)
            {
                var treeViewItem = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeViewItem?.DataContext == node)
                    return treeViewItem;

                var childItem = FindTreeViewItem(treeViewItem, node);
                if (childItem != null)
                    return childItem;
            }
            return null;
        }

        private void TreeViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                // Attempt to find the "Expander" toggle button provided by the template
                var expander = FindVisualChild<System.Windows.Controls.Primitives.ToggleButton>(item, "Expander");
                if (expander != null)
                {
                    // Update initial state
                    UpdateExpanderTooltip(expander);

                    // Hook up events to keep it dynamic
                    expander.Checked -= Expander_StateChanged;
                    expander.Unchecked -= Expander_StateChanged;
                    expander.Checked += Expander_StateChanged;
                    expander.Unchecked += Expander_StateChanged;
                }
            }
        }

        private void Expander_StateChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton expander)
            {
                UpdateExpanderTooltip(expander);
            }
        }

        private void UpdateExpanderTooltip(System.Windows.Controls.Primitives.ToggleButton expander)
        {
            expander.ToolTip = expander.IsChecked == true ? "Collapse" : "Expand";
        }

        private static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                // If a name is specified, ensure it matches
                if (!string.IsNullOrEmpty(childName))
                {
                    if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName)
                    {
                        if (child is T typedChild) return typedChild;
                    }
                }
                else if (child is T typedChild)
                {
                    return typedChild;
                }

                var foundChild = FindVisualChild<T>(child, childName);
                if (foundChild != null) return foundChild;
            }
            return null;
        }

    }
}
