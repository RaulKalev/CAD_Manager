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
using System.Windows.Interop;
using System.Windows.Threading;

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
        private readonly WindowCoordinator _windowCoordinator;
        private readonly CommandButtons _commandButtons;
        private readonly TreeViewControls _treeViewControls;
        private readonly ColorOverrideHandler _colorOverrideHandler;
        private readonly ExternalEvent _colorOverrideEvent;
        private readonly LineGraphicsReadHandler _lineGraphicsReadHandler;
        private readonly ExternalEvent _lineGraphicsReadEvent;
        private readonly ThemeManager _themeManager;
        private ApplyToViewsWindow _applyToViewsWindow;
        private LineGraphicsWindow _lineGraphicsWindow;
        private LineGraphicsSession _lineGraphicsSession;
        private bool _applyToViewsPending;
        private bool _suppressTreeOperationEvents;
        private HwndSource _windowSource;
        private bool _allowExplicitMinimize;
        private DispatcherTimer _statusTimer;
        private bool _notificationVisible;
        private string _contextSummary = "Active view  ·  No selection";

        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int EscapeVirtualKey = 0x1B;

        public CADManagerWindow(List<DWGNode> dwgNodes, UIDocument uiDoc)
        {
            // The window XAML derives local styles from theme resources while it
            // is being parsed. AppLoader does not provide an App.xaml resource
            // dictionary, so install a bootstrap theme before InitializeComponent.
            if (Application.ResourceAssembly == null)
            {
                Application.ResourceAssembly = Assembly.GetExecutingAssembly();
            }

            Resources.MergedDictionaries.Add(ThemeManager.CreateThemeDictionary(true));
            InitializeComponent();

            DWGNodes = dwgNodes;
            _uiDoc = uiDoc;
            BuildLayerParentLookup();

            _visibilityToggler = new VisibilityToggler
            {
                DWGNodes = DWGNodes,
                Document = _uiDoc.Document,
                CurrentView = _uiDoc.Document.ActiveView
            };

            _externalEvent = ExternalEvent.Create(_visibilityToggler);
            _treeViewControls = new TreeViewControls();

            _colorOverrideHandler = new ColorOverrideHandler();
            _colorOverrideEvent = ExternalEvent.Create(_colorOverrideHandler);

            _lineGraphicsReadHandler = new LineGraphicsReadHandler();
            _lineGraphicsReadEvent = ExternalEvent.Create(_lineGraphicsReadHandler);

            _halftoneHandler = new HalftoneHandler
            {
                Document = _uiDoc.Document,
                CurrentView = _uiDoc.Document.ActiveView
            };
            _halftoneEvent = ExternalEvent.Create(_halftoneHandler);

            _applyToViewsHandler = new ApplyToViewsHandler();
            _applyToViewsEvent = ExternalEvent.Create(_applyToViewsHandler);

            _themeManager = new ThemeManager(this, ShowStatus);
            _windowCoordinator = new WindowCoordinator(this);
            _themeManager.ThemeResourcesChanged += ThemeManager_ThemeResourcesChanged;

            _treeViewControls.SortDWGs(DWGNodes);

            FilteredDWGNodes = DWGNodes;

            _commandButtons = new CommandButtons(
                _uiDoc,
                _externalEvent,
                _visibilityToggler,
                RefreshTreeView,
                this,
                ShowStatus);

            DWGTreeView.ItemsSource = FilteredDWGNodes;
            DWGTreeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
            DWGTreeView.PreviewKeyDown += DWGTreeView_PreviewKeyDown;
            this.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);

            this.Closed += Window1_Closed;

            DWGTreeView.Loaded += (s, e) => _treeViewControls.ExpandAllNodes(DWGNodes);

            _themeManager.LoadThemeState();
            ThemeToggleButton.IsChecked = _themeManager.IsDarkMode;
            PinToggleButton.IsChecked = _themeManager.IsPinned;
            Topmost = _themeManager.IsPinned;
            _themeManager.LoadTheme();

            // Set initial icon state
            ThemeToggleIcon.Kind = _themeManager.IsDarkMode
                ? MaterialDesignThemes.Wpf.PackIconKind.WeatherNight
                : MaterialDesignThemes.Wpf.PackIconKind.WhiteBalanceSunny;
            UpdatePinIcon();
            UpdateContextSummary();

            this.Focusable = true;
            this.Focus();
            
            if (_uiDoc.Document.IsFamilyDocument)
            {
                DisableFileButtons();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMaximizedState();
            this.Focus();
            Keyboard.Focus(this);

            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_windowSource != null)
                _windowSource.AddHook(WindowHwndHook);
        }

        private IntPtr WindowHwndHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if ((message == WmKeyDown || message == WmSysKeyDown)
                && wParam.ToInt64() == EscapeVirtualKey)
            {
                HandleEscapeKey();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void HandleEscapeKey()
        {
            ClearTreeViewSelection();
            if (SearchBox != null)
                SearchBox.Text = string.Empty;
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            _allowExplicitMinimize = true;
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == System.Windows.WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void Window_StateChanged(object sender, System.EventArgs e)
        {
            if (WindowState == System.Windows.WindowState.Minimized && !_allowExplicitMinimize)
            {
                WindowState = System.Windows.WindowState.Normal;
                return;
            }

            if (WindowState != System.Windows.WindowState.Minimized)
                _allowExplicitMinimize = false;

            UpdateMaximizedState();
        }

        private void UpdateMaximizedState()
        {
            bool isMaximized = WindowState == System.Windows.WindowState.Maximized;
            Thickness frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = isMaximized
                ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
                : new Thickness(0);

            MaximizeIcon.Kind = isMaximized
                ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore
                : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
            MaximizeButton.ToolTip = isMaximized ? "Restore down" : "Maximize";
            System.Windows.Automation.AutomationProperties.SetName(
                MaximizeButton,
                isMaximized ? "Restore CAD Manager" : "Maximize CAD Manager");
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }

        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _themeManager.IsDarkMode = ThemeToggleButton.IsChecked == true;
            _themeManager.LoadTheme();
            _windowCoordinator.ApplyToOpenWindows(_themeManager.LoadTheme);

            ThemeToggleIcon.Kind = _themeManager.IsDarkMode
                ? MaterialDesignThemes.Wpf.PackIconKind.WeatherNight
                : MaterialDesignThemes.Wpf.PackIconKind.WhiteBalanceSunny;

            if (_themeManager.IsHighContrastActive)
                ShowStatus("Windows High Contrast is active. CAD Manager will keep using the system contrast palette.", false, true);
        }

        private void ThemeManager_ThemeResourcesChanged(object sender, System.EventArgs e)
        {
            _windowCoordinator.ApplyToOpenWindows(_themeManager.LoadTheme);
            ShowStatus(
                _themeManager.IsHighContrastActive
                    ? "Windows High Contrast palette enabled."
                    : "Windows High Contrast palette disabled.",
                false,
                true);
        }

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            bool isPinned = PinToggleButton.IsChecked == true;
            Topmost = isPinned;
            _themeManager.IsPinned = isPinned;
            UpdatePinIcon();
            _themeManager.SaveThemeState();
        }

        private void ShowShortcuts_Click(object sender, RoutedEventArgs e)
        {
            ShowStatus("Tree shortcuts: Ctrl-click adds or removes rows; Shift-click selects a range; Ctrl+A selects the current DWG’s layers; Esc clears selection and search.", false, false);
        }

        private void UpdatePinIcon()
        {
            PinToggleIcon.Kind = Topmost
                ? MaterialDesignThemes.Wpf.PackIconKind.Pin
                : MaterialDesignThemes.Wpf.PackIconKind.PinOutline;
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
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WindowHwndHook);
                _windowSource = null;
            }

            _themeManager.SaveThemeState();
            _themeManager.ThemeResourcesChanged -= ThemeManager_ThemeResourcesChanged;
            _themeManager.Dispose();

            DWGTreeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
            DWGTreeView.Loaded -= (s, args) => _treeViewControls.ExpandAllNodes(DWGNodes);

            _visibilityToggler?.CancelPendingRequest();
            _externalEvent?.Dispose();
            _colorOverrideEvent?.Dispose();
            _lineGraphicsReadEvent?.Dispose();
            _halftoneHandler?.CancelPendingRequest();
            _halftoneEvent?.Dispose();
            _applyToViewsEvent?.Dispose();
            _windowCoordinator?.Dispose();
            _statusTimer?.Stop();
            _statusTimer = null;
            _visibilityToggler.DWGNodes = null;
            _visibilityToggler.Document = null;
            _visibilityToggler.CurrentView = null;

            DWGNodes?.Clear();
            FilteredDWGNodes?.Clear();
            DWGTreeView.ItemsSource = null;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Search.ClearSearchBox(SearchBox);

        private void CheckBox_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressTreeOperationEvents || !(sender is CheckBox checkBox))
                return;

            List<TreeVisibilityChange> changes;
            _suppressTreeOperationEvents = true;
            try
            {
                changes = _treeViewControls.PrepareVisibilityChanges(checkBox, FilteredDWGNodes);
            }
            finally
            {
                _suppressTreeOperationEvents = false;
            }

            if (changes.Count == 0)
                return;

            List<VisibilityChangeTarget> targets = BuildVisibilityTargets(changes);
            if (targets.Count != changes.Count)
            {
                CompleteVisibilityChanges(changes, false, "The selected DWG context could not be resolved. Refresh and try again.");
                return;
            }

            SetVisibilityPending(changes, true, null);
            Document document = _uiDoc?.Document;
            View view = document?.ActiveView;
            bool submitted = _visibilityToggler.TrySubmit(
                document,
                view?.Id,
                targets,
                message => CompleteVisibilityChanges(changes, true, message),
                message => CompleteVisibilityChanges(changes, false, message));

            if (!submitted)
            {
                CompleteVisibilityChanges(changes, false, "Another visibility update is already pending in Revit.");
                return;
            }

            ExternalEventRequest raiseResult = _externalEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _visibilityToggler.CancelPendingRequest();
                CompleteVisibilityChanges(changes, false, "Revit could not queue the visibility update. Try again when Revit is idle.");
            }
        }

        private void Halftone_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressTreeOperationEvents ||
                !(sender is System.Windows.Controls.Primitives.ToggleButton toggleButton) ||
                !(toggleButton.DataContext is DWGNode clickedNode))
            {
                return;
            }

            bool newState = toggleButton.IsChecked == true;
            List<DWGNode> nodes = clickedNode.IsSelected
                ? FilteredDWGNodes.Where(node => node.IsSelected).ToList()
                : new List<DWGNode> { clickedNode };
            List<HalftoneUiChange> changes = nodes
                .Select(node => new HalftoneUiChange(
                    node,
                    ReferenceEquals(node, clickedNode) ? !newState : node.IsHalftone,
                    newState))
                .ToList();

            _suppressTreeOperationEvents = true;
            try
            {
                foreach (HalftoneUiChange change in changes)
                {
                    change.Node.IsHalftone = change.NewValue;
                    change.Node.IsHalftonePending = true;
                    change.Node.OperationError = null;
                    SyncFilteredNodeToOriginal(change.Node);
                }
            }
            finally
            {
                _suppressTreeOperationEvents = false;
            }

            Document document = _uiDoc?.Document;
            View view = document?.ActiveView;
            List<HalftoneChangeTarget> targets = changes
                .Where(change => change.Node.ElementId != null)
                .Select(change => new HalftoneChangeTarget(change.Node.ElementId, change.NewValue))
                .ToList();

            if (targets.Count != changes.Count)
            {
                CompleteHalftoneChanges(changes, false, "The selected DWG context could not be resolved. Refresh and try again.");
                return;
            }

            bool submitted = _halftoneHandler.TrySubmit(
                document,
                view?.Id,
                targets,
                message => CompleteHalftoneChanges(changes, true, message),
                message => CompleteHalftoneChanges(changes, false, message));

            if (!submitted)
            {
                CompleteHalftoneChanges(changes, false, "Another halftone update is already pending in Revit.");
                return;
            }

            ExternalEventRequest raiseResult = _halftoneEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _halftoneHandler.CancelPendingRequest();
                CompleteHalftoneChanges(changes, false, "Revit could not queue the halftone update. Try again when Revit is idle.");
            }
        }

        private List<VisibilityChangeTarget> BuildVisibilityTargets(IEnumerable<TreeVisibilityChange> changes)
        {
            List<VisibilityChangeTarget> targets = new List<VisibilityChangeTarget>();
            foreach (TreeVisibilityChange change in changes)
            {
                if (change.Node is DWGNode dwgNode && dwgNode.ElementId != null)
                {
                    targets.Add(new VisibilityChangeTarget(dwgNode.ElementId, null, change.NewValue));
                }
                else if (change.Node is LayerNode layerNode)
                {
                    DWGNode parent = FindParentDWGNode(layerNode);
                    if (parent?.ElementId != null)
                        targets.Add(new VisibilityChangeTarget(parent.ElementId, layerNode.Name, change.NewValue));
                }
            }
            return targets;
        }

        private void SetVisibilityPending(
            IEnumerable<TreeVisibilityChange> changes,
            bool isPending,
            string error)
        {
            foreach (TreeVisibilityChange change in changes)
            {
                if (change.Node is DWGNode dwgNode)
                {
                    dwgNode.IsVisibilityPending = isPending;
                    dwgNode.OperationError = error;
                    SyncFilteredNodeToOriginal(dwgNode);
                }
                else if (change.Node is LayerNode layerNode)
                {
                    layerNode.IsVisibilityPending = isPending;
                    layerNode.OperationError = error;
                    SyncFilteredLayerToOriginal(layerNode);
                }
            }
        }

        private void CompleteVisibilityChanges(
            List<TreeVisibilityChange> changes,
            bool succeeded,
            string message)
        {
            _suppressTreeOperationEvents = true;
            try
            {
                if (!succeeded)
                {
                    foreach (TreeVisibilityChange change in changes)
                    {
                        if (change.Node is DWGNode dwgNode)
                            dwgNode.IsChecked = change.PreviousValue;
                        else if (change.Node is LayerNode layerNode)
                            layerNode.IsChecked = change.PreviousValue;
                    }
                }

                SetVisibilityPending(changes, false, succeeded ? null : message);
            }
            finally
            {
                _suppressTreeOperationEvents = false;
            }

            ShowStatus(message, !succeeded, succeeded);
        }

        private void CompleteHalftoneChanges(
            List<HalftoneUiChange> changes,
            bool succeeded,
            string message)
        {
            _suppressTreeOperationEvents = true;
            try
            {
                foreach (HalftoneUiChange change in changes)
                {
                    if (!succeeded)
                        change.Node.IsHalftone = change.PreviousValue;

                    change.Node.IsHalftonePending = false;
                    change.Node.OperationError = succeeded ? null : message;
                    SyncFilteredNodeToOriginal(change.Node);
                }
            }
            finally
            {
                _suppressTreeOperationEvents = false;
            }

            ShowStatus(message, !succeeded, succeeded);
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
                original.IsVisibilityPending = filteredNode.IsVisibilityPending;
                original.IsHalftonePending = filteredNode.IsHalftonePending;
                original.OperationError = filteredNode.OperationError;
                
                // Note: Layers are separate objects in the clone, but we handle them specifically in SyncFilteredLayerToOriginal
            }
        }

        private void SyncFilteredLayerToOriginal(LayerNode filteredLayer)
        {
            if (filteredLayer == null || DWGNodes == null) return;

            // Find parent DWG using the cache
             var parentDwg = FindParentDWGNode(filteredLayer);
            if (parentDwg == null) return; // Can't find parent in current view context

            // Start from the original DWG list to find the original LayerNode
            // We match by DWG ElementId first, then Layer Name
            var originalDwg = DWGNodes.FirstOrDefault(n => n.ElementId == parentDwg.ElementId);
            if (originalDwg != null)
            {
                 var originalLayer = originalDwg.Layers.FirstOrDefault(l => l.Name == filteredLayer.Name);
                 if (originalLayer != null && !ReferenceEquals(originalLayer, filteredLayer))
                 {
                     originalLayer.IsChecked = filteredLayer.IsChecked;
                     originalLayer.LineColor = filteredLayer.LineColor;
                     originalLayer.LinePattern = filteredLayer.LinePattern;
                     originalLayer.LineWeight = filteredLayer.LineWeight;
                     originalLayer.IsSelected = filteredLayer.IsSelected;
                     originalLayer.IsVisibilityPending = filteredLayer.IsVisibilityPending;
                     originalLayer.OperationError = filteredLayer.OperationError;
                 }
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e) => Search.HandleSearchBoxGotFocus(SearchBox);
        private void SearchBox_LostFocus(object sender, RoutedEventArgs e) => Search.HandleSearchBoxLostFocus(SearchBox);
        
        private void RefreshTreeView()
        {
            _treeViewControls.RefreshTreeView(DWGTreeView, FilteredDWGNodes);
            BuildLayerParentLookup();
            UpdateContextSummary();
        }
        
        private void LoadButton_Click(object sender, RoutedEventArgs e) => _commandButtons.LoadButton_Click(sender, e);
        private void SaveButton_Click(object sender, RoutedEventArgs e) => _commandButtons.SaveButton_Click(sender, e);
        private void BrowseButton_Click(object sender, RoutedEventArgs e) => _commandButtons.BrowseButton_Click(sender, e);

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _commandButtons.RefreshButton_Click(sender, e);

        private void ApplyToViewsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_applyToViewsPending && (_applyToViewsWindow == null || !_applyToViewsWindow.IsVisible))
            {
                ShowStatus("Apply to Views is already processing a request.", false);
                return;
            }

            Document document = _uiDoc?.Document;
            View sourceView = document?.ActiveView;
            if (document == null || sourceView == null)
            {
                ShowStatus("Apply to Views is unavailable because there is no active document or view.", true);
                return;
            }

            _applyToViewsWindow = _windowCoordinator.ShowOrActivate(
                "ApplyToViews",
                () => new ApplyToViewsWindow(document, sourceView),
                window =>
                {
                    window.ApplyRequested += ApplyToViewsWindow_ApplyRequested;
                    window.Closed += ApplyToViewsWindow_Closed;
                });
        }

        private void ApplyToViewsWindow_ApplyRequested(object sender, ApplyToViewsRequestedEventArgs e)
        {
            if (_applyToViewsPending)
            {
                _applyToViewsWindow?.SetRequestState(true, "An Apply to Views request is already pending.");
                return;
            }

            bool submitted = _applyToViewsHandler.TrySubmit(
                e.Document,
                e.SourceViewId,
                e.TargetViewIds,
                ApplyToViewsCompleted,
                ApplyToViewsFailed);

            if (!submitted)
            {
                _applyToViewsWindow?.SetRequestState(false, "The request could not be queued. Check the selected views and try again.", true);
                return;
            }

            _applyToViewsPending = true;
            _applyToViewsWindow?.SetRequestState(true, "Applying settings in Revit…");
            ShowStatus("Applying settings to selected views…", false);

            ExternalEventRequest raiseResult = _applyToViewsEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _applyToViewsHandler.CancelPendingRequest();
                _applyToViewsPending = false;
                string message = "Revit could not queue Apply to Views. Try again when Revit is idle.";
                _applyToViewsWindow?.SetRequestState(false, message, true);
                ShowStatus(message, true);
            }
        }

        private void ApplyToViewsCompleted(string report)
        {
            _applyToViewsPending = false;
            _applyToViewsWindow?.SetRequestState(false, report);
            ShowStatus("Settings were applied to the selected views.", false, true);
        }

        private void ApplyToViewsFailed(string message)
        {
            _applyToViewsPending = false;
            _applyToViewsWindow?.SetRequestState(false, message, true);
            ShowStatus(message, true);
        }

        private void ApplyToViewsWindow_Closed(object sender, EventArgs e)
        {
            if (sender is ApplyToViewsWindow window)
            {
                window.ApplyRequested -= ApplyToViewsWindow_ApplyRequested;
                window.Closed -= ApplyToViewsWindow_Closed;
            }

            _applyToViewsWindow = null;
        }

        private void ShowStatus(string message, bool isError, bool autoDismiss = false)
        {
            // Keep the existing call signature for command callbacks, but present
            // each notification in the single-line footer for a predictable 15 seconds.
            _statusTimer?.Stop();
            _notificationVisible = true;
            StatusText.Text = isError ? $"Error: {message}" : message;
            string brushKey = isError ? "ErrorBrush" : "ForegroundBrush";
            StatusText.Foreground = (System.Windows.Media.Brush)(TryFindResource(brushKey)
                ?? System.Windows.Media.Brushes.Black);
            StatusBorder.Visibility = System.Windows.Visibility.Visible;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _statusTimer.Tick += (timerSender, timerArgs) => RestoreContextSummary();
            _statusTimer.Start();
        }

        private void RestoreContextSummary()
        {
            _statusTimer?.Stop();
            _statusTimer = null;
            _notificationVisible = false;
            StatusText.Text = _contextSummary;
            StatusText.Foreground = (System.Windows.Media.Brush)(TryFindResource("ForegroundBrush")
                ?? System.Windows.Media.Brushes.Black);
            StatusBorder.Visibility = System.Windows.Visibility.Visible;
        }

        private void TreeViewItem_EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button button) || button.Tag == null)
                return;

            LineGraphicsSession session = CreateLineGraphicsSession(button.Tag);
            if (session == null)
            {
                ShowStatus("Line graphics could not resolve the selected DWG context.", true, false);
                return;
            }

            OpenLineGraphicsInspector(session);
        }

        private LineGraphicsSession CreateLineGraphicsSession(object node)
        {
            Document document = _uiDoc?.Document;
            View activeView = document?.ActiveView;
            if (document == null || activeView == null)
                return null;

            if (node is DWGNode clickedDwgNode && clickedDwgNode.ElementId != null)
            {
                List<DWGNode> nodes = clickedDwgNode.IsSelected
                    ? FilteredDWGNodes.Where(item => item.IsSelected && item.ElementId != null).ToList()
                    : new List<DWGNode> { clickedDwgNode };
                if (nodes.Count == 0)
                    return null;

                List<LineGraphicsTarget> targets = nodes
                    .Select(item => new LineGraphicsTarget(item.ElementId, null))
                    .ToList();
                string noun = nodes.Count == 1 ? "DWG" : "DWGs";
                return new LineGraphicsSession(
                    document,
                    activeView.Id,
                    targets,
                    false,
                    $"{nodes.Count} {noun} in the active view",
                    nodes,
                    null);
            }

            if (!(node is LayerNode clickedLayerNode))
                return null;

            List<LayerNode> layers = clickedLayerNode.IsSelected
                ? FilteredDWGNodes.SelectMany(dwg => dwg.Layers).Where(layer => layer.IsSelected).ToList()
                : new List<LayerNode> { clickedLayerNode };

            Dictionary<DWGNode, List<LayerNode>> layersByDwg = new Dictionary<DWGNode, List<LayerNode>>();
            foreach (LayerNode layer in layers)
            {
                DWGNode parent = FindParentDWGNode(layer);
                if (parent == null || parent.ElementId == null)
                    continue;

                if (!layersByDwg.TryGetValue(parent, out List<LayerNode> group))
                {
                    group = new List<LayerNode>();
                    layersByDwg[parent] = group;
                }
                group.Add(layer);
            }

            if (layersByDwg.Count == 0)
                return null;

            List<LineGraphicsTarget> layerTargets = layersByDwg
                .Select(pair => new LineGraphicsTarget(pair.Key.ElementId, pair.Value.Select(layer => layer.Name)))
                .ToList();
            List<LayerNode> resolvedLayers = layersByDwg.SelectMany(pair => pair.Value).ToList();
            string layerNoun = resolvedLayers.Count == 1 ? "layer" : "layers";
            string dwgNoun = layersByDwg.Count == 1 ? "DWG" : "DWGs";
            return new LineGraphicsSession(
                document,
                activeView.Id,
                layerTargets,
                true,
                $"{resolvedLayers.Count} {layerNoun} in {layersByDwg.Count} {dwgNoun}",
                null,
                resolvedLayers);
        }

        private void OpenLineGraphicsInspector(LineGraphicsSession session)
        {
            LineGraphicsWindow window = _windowCoordinator.ShowOrActivate(
                "line-graphics",
                () => new LineGraphicsWindow(_themeManager),
                createdWindow =>
                {
                    createdWindow.ApplyRequested += LineGraphicsWindow_ApplyRequested;
                    createdWindow.Closed += LineGraphicsWindow_Closed;
                });

            _lineGraphicsWindow = window;
            if (!window.TryBeginLoad(session.ScopeDescription))
            {
                ShowStatus("Finish or close the current Line Graphics edits before changing its scope.", true, false);
                return;
            }

            _lineGraphicsSession = session;
            bool accepted = _lineGraphicsReadHandler.TrySubmit(
                session.Document,
                session.ViewId,
                session.Targets,
                session.IsLayerOverride,
                snapshot =>
                {
                    if (ReferenceEquals(_lineGraphicsSession, session))
                        _lineGraphicsWindow?.LoadSnapshot(snapshot);
                },
                message =>
                {
                    if (ReferenceEquals(_lineGraphicsSession, session))
                        _lineGraphicsWindow?.SetRequestState(false, message, true);
                    ShowStatus(message, true, false);
                });

            if (!accepted)
            {
                window.SetRequestState(false, "Another graphics state request is already pending.", true);
                return;
            }

            ExternalEventRequest raiseResult = _lineGraphicsReadEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _lineGraphicsReadHandler.CancelPendingRequest();
                window.SetRequestState(false, "Revit could not queue the graphics state request. Try again.", true);
            }
        }

        private void LineGraphicsWindow_ApplyRequested(object sender, LineGraphicsApplyRequestedEventArgs e)
        {
            LineGraphicsSession session = _lineGraphicsSession;
            if (session == null || _lineGraphicsWindow == null)
                return;

            bool accepted = _colorOverrideHandler.TrySubmit(
                session.Document,
                session.ViewId,
                session.Targets,
                session.IsLayerOverride,
                e.Color,
                e.Pattern,
                e.Weight,
                e.ClearOverrides,
                message =>
                {
                    string completionMessage = e.ClearOverrides
                        ? $"{message} Use Revit Undo to restore the previous overrides."
                        : $"{message} Use Revit Undo to revert this change.";
                    ApplyLineGraphicsToLocalModel(session, e);
                    _lineGraphicsWindow?.MarkApplied(e.ClearOverrides, completionMessage);
                    ShowStatus(completionMessage, false, true);
                },
                message =>
                {
                    _lineGraphicsWindow?.SetRequestState(false, message, true);
                    ShowStatus(message, true, false);
                });

            if (!accepted)
            {
                _lineGraphicsWindow.SetRequestState(false, "Another line graphics update is already pending.", true);
                return;
            }

            _lineGraphicsWindow.SetRequestState(true, e.ClearOverrides
                ? "Clearing overrides…"
                : "Applying line graphics…");

            ExternalEventRequest raiseResult = _colorOverrideEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _colorOverrideHandler.CancelPendingRequest();
                _lineGraphicsWindow.SetRequestState(false, "Revit could not queue the graphics update. Try again.", true);
            }
        }

        private void ApplyLineGraphicsToLocalModel(LineGraphicsSession session, LineGraphicsApplyRequestedEventArgs request)
        {
            IEnumerable<object> items = session.IsLayerOverride
                ? session.Layers.Cast<object>()
                : session.DwgNodes.Cast<object>();

            foreach (object item in items)
            {
                if (item is DWGNode dwgNode)
                {
                    UpdateLocalGraphics(dwgNode, request);
                    SyncFilteredNodeToOriginal(dwgNode);
                }
                else if (item is LayerNode layerNode)
                {
                    UpdateLocalGraphics(layerNode, request);
                }
            }
        }

        private static void UpdateLocalGraphics(DWGNode node, LineGraphicsApplyRequestedEventArgs request)
        {
            if (request.ClearOverrides)
            {
                node.LineColor = null;
                node.LinePattern = null;
                node.LineWeight = null;
                return;
            }

            if (request.Color != null)
                node.LineColor = $"#{request.Color.Red:X2}{request.Color.Green:X2}{request.Color.Blue:X2}";
            if (request.Pattern != null)
                node.LinePattern = request.Pattern == "<No Override>" ? null : request.Pattern;
            if (request.Weight.HasValue)
                node.LineWeight = request.Weight.Value == -1 ? (int?)null : request.Weight.Value;
        }

        private static void UpdateLocalGraphics(LayerNode node, LineGraphicsApplyRequestedEventArgs request)
        {
            if (request.ClearOverrides)
            {
                node.LineColor = null;
                node.LinePattern = null;
                node.LineWeight = null;
                return;
            }

            if (request.Color != null)
                node.LineColor = $"#{request.Color.Red:X2}{request.Color.Green:X2}{request.Color.Blue:X2}";
            if (request.Pattern != null)
                node.LinePattern = request.Pattern == "<No Override>" ? null : request.Pattern;
            if (request.Weight.HasValue)
                node.LineWeight = request.Weight.Value == -1 ? (int?)null : request.Weight.Value;
        }

        private void LineGraphicsWindow_Closed(object sender, EventArgs e)
        {
            if (sender is LineGraphicsWindow window)
            {
                window.ApplyRequested -= LineGraphicsWindow_ApplyRequested;
                window.Closed -= LineGraphicsWindow_Closed;
            }

            _lineGraphicsWindow = null;
            _lineGraphicsSession = null;
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


        public void UpdateContext(Document doc, View view)
        {
            if (_visibilityToggler != null)
            {
                _visibilityToggler.Document = doc;
                _visibilityToggler.CurrentView = view;
            }

            if (_halftoneHandler != null)
            {
                _halftoneHandler.Document = doc;
                _halftoneHandler.CurrentView = view;
            }

            UpdateContextSummary();
        }

        private void UpdateContextSummary()
        {
            if (StatusText == null)
                return;

            View currentView = _visibilityToggler?.CurrentView ?? _uiDoc?.Document?.ActiveView;
            string viewName = currentView != null && !string.IsNullOrWhiteSpace(currentView.Name)
                ? currentView.Name
                : "No active view";

            int selectedCount = 0;
            if (DWGNodes != null)
            {
                selectedCount = DWGNodes.Count(node => node.IsSelected)
                    + DWGNodes.Sum(node => node.Layers?.Count(layer => layer.IsSelected) ?? 0);
            }

            string selectionSummary = selectedCount == 0
                ? "No selection"
                : selectedCount == 1 ? "1 selected" : $"{selectedCount} selected";

            string nextSummary = $"{viewName}  ·  {selectionSummary}";
            bool contextChanged = !string.Equals(_contextSummary, nextSummary, StringComparison.Ordinal);
            _contextSummary = nextSummary;

            if (contextChanged && _notificationVisible)
            {
                RestoreContextSummary();
                return;
            }

            if (!_notificationVisible)
                RestoreContextSummary();
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

            foreach (DWGNode filteredNode in FilteredDWGNodes ?? Enumerable.Empty<DWGNode>())
            {
                filteredNode.IsSelected = false;
                foreach (LayerNode layer in filteredNode.Layers)
                    layer.IsSelected = false;
            }

            _lastSelectedNode = null;
            UpdateContextSummary();
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

            UpdateContextSummary();
        }

        private void SelectRange(DWGNode startNode, DWGNode endNode)
        {
            bool inRange = false;
            foreach (DWGNode node in FilteredDWGNodes ?? DWGNodes)
            {
                if (node == startNode || node == endNode)
                {
                    SetDwgSelection(node, true);
                    inRange = !inRange;
                }
                if (inRange || node == startNode || node == endNode)
                {
                    SetDwgSelection(node, true);
                }
            }
        }

        private void SelectRangeLayers(LayerNode startNode, LayerNode endNode)
        {
            bool inRange = false;
            foreach (DWGNode dwgNode in FilteredDWGNodes ?? DWGNodes)
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
                    SetDwgSelection(dwgNode, !dwgNode.IsSelected);
                    break;
                case LayerNode layerNode:
                    layerNode.IsSelected = !layerNode.IsSelected;
                    break;
            }
        }

        private void SelectSingle(object node)
        {
            foreach (DWGNode dwgNode in DWGNodes)
            {
                dwgNode.IsSelected = false;
                foreach (LayerNode layer in dwgNode.Layers)
                    layer.IsSelected = false;
            }

            foreach (DWGNode filteredNode in FilteredDWGNodes ?? Enumerable.Empty<DWGNode>())
            {
                filteredNode.IsSelected = false;
                foreach (LayerNode layer in filteredNode.Layers)
                    layer.IsSelected = false;
            }

            switch (node)
            {
                case DWGNode dwgNode:
                    SetDwgSelection(dwgNode, true);
                    break;
                case LayerNode layerNode:
                    layerNode.IsSelected = true;
                    break;
            }
        }

        private void SetDwgSelection(DWGNode node, bool isSelected)
        {
            if (node == null)
                return;

            node.IsSelected = isSelected;
            DWGNode original = DWGNodes?.FirstOrDefault(item =>
                item.ElementId != null && node.ElementId != null &&
                item.ElementId.GetIdValue() == node.ElementId.GetIdValue());
            if (original != null)
                original.IsSelected = isSelected;

            DWGNode filtered = FilteredDWGNodes?.FirstOrDefault(item =>
                item.ElementId != null && node.ElementId != null &&
                item.ElementId.GetIdValue() == node.ElementId.GetIdValue());
            if (filtered != null)
                filtered.IsSelected = isSelected;
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
            // Consume Escape inside the modeless window so Revit does not treat
            // it as a command-cancel/minimize gesture. Keep the local cleanup
            // behavior without allowing the key to escape to the host.
            if (e.Key == Key.Escape)
            {
                HandleEscapeKey();
                e.Handled = true;
                return;
            }

            // Allow Ctrl+A unless the user is typing in the search box
            if (SearchBox.IsKeyboardFocusWithin)
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
                    string itemName = (item.DataContext as DWGNode)?.Name ?? "DWG";
                    System.Windows.Automation.AutomationProperties.SetName(expander, $"Expand or collapse {itemName}");

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

        private sealed class HalftoneUiChange
        {
            public HalftoneUiChange(DWGNode node, bool previousValue, bool newValue)
            {
                Node = node;
                PreviousValue = previousValue;
                NewValue = newValue;
            }

            public DWGNode Node { get; }
            public bool PreviousValue { get; }
            public bool NewValue { get; }
        }

        private sealed class LineGraphicsSession
        {
            public LineGraphicsSession(
                Document document,
                ElementId viewId,
                List<LineGraphicsTarget> targets,
                bool isLayerOverride,
                string scopeDescription,
                List<DWGNode> dwgNodes,
                List<LayerNode> layers)
            {
                Document = document;
                ViewId = viewId;
                Targets = targets;
                IsLayerOverride = isLayerOverride;
                ScopeDescription = scopeDescription;
                DwgNodes = dwgNodes ?? new List<DWGNode>();
                Layers = layers ?? new List<LayerNode>();
            }

            public Document Document { get; }
            public ElementId ViewId { get; }
            public List<LineGraphicsTarget> Targets { get; }
            public bool IsLayerOverride { get; }
            public string ScopeDescription { get; }
            public List<DWGNode> DwgNodes { get; }
            public List<LayerNode> Layers { get; }
        }

    }
}
