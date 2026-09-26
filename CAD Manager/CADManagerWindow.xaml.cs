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
using CAD_Manager.Core;
using System.Windows.Media;
using System.Windows.Interop;
using System.IO;

namespace CAD_Manager
{
    public partial class CADManagerWindow : Window
    {
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
        private readonly OperationStatusController _operationStatus;
        private readonly SelectionController<DWGNode, LayerNode> _selectionController;
        private readonly CommandButtons _commandButtons;
        private readonly TreeViewControls _treeViewControls;
        private readonly ColorOverrideHandler _colorOverrideHandler;
        private readonly ExternalEvent _colorOverrideEvent;
        private readonly LineGraphicsReadHandler _lineGraphicsReadHandler;
        private readonly ExternalEvent _lineGraphicsReadEvent;
        private readonly ThemeManager _themeManager;
        private readonly LayerSelectionFilterStore _layerSelectionFilterStore;
        private readonly TreeAutoWidthController _treeAutoWidth;
        private IReadOnlyList<LayerSelectionFilterRule> _layerSelectionFilters;
        private ApplyToViewsWindow _applyToViewsWindow;
        private LineGraphicsWindow _lineGraphicsWindow;
        private LayerSelectionFiltersWindow _layerSelectionFiltersWindow;
        private SettingsWindow _settingsWindow;
        private LineGraphicsSession _lineGraphicsSession;
        private bool _applyToViewsPending;
        private bool _suppressTreeOperationEvents;
        private HwndSource _windowSource;
        private bool _allowExplicitMinimize;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int WmSysCommand = 0x0112;
        private const int EscapeVirtualKey = 0x1B;
        private const int ScMinimize = 0xF020;

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
            _selectionController = new SelectionController<DWGNode, LayerNode>(
                () => DWGNodes ?? Enumerable.Empty<DWGNode>(),
                () => FilteredDWGNodes ?? Enumerable.Empty<DWGNode>(),
                node => node.Layers ?? Enumerable.Empty<LayerNode>(),
                node => node.IsSelected,
                (node, value) => node.IsSelected = value,
                node => node.IsSelected,
                (node, value) => node.IsSelected = value,
                AreSameDwg,
                (left, right) => ReferenceEquals(left, right));
            _operationStatus = new OperationStatusController(RenderOperationStatus);
            _layerSelectionFilterStore = new LayerSelectionFilterStore();
            try
            {
                _layerSelectionFilters = _layerSelectionFilterStore.Load();
            }
            catch (Exception ex)
            {
                _layerSelectionFilters = new List<LayerSelectionFilterRule>();
                ShowStatus($"Layer selection filters could not be loaded: {ex.Message}", true, false);
            }

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
                ShowStatus,
                ApplyPresetStateToUi);

            DWGTreeView.ItemsSource = FilteredDWGNodes;
            _treeAutoWidth = new TreeAutoWidthController(this, DWGTreeView, () => FilteredDWGNodes);
            DWGTreeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
            this.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);

            this.Closed += Window1_Closed;

            DWGTreeView.Loaded += DWGTreeView_Loaded;

            _themeManager.LoadThemeState();
            PinToggleButton.IsChecked = _themeManager.IsPinned;
            Topmost = _themeManager.IsPinned;
            _themeManager.LoadTheme();

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

            ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;

            // Size before the first render so the window does not visibly jump.
            _treeViewControls.ExpandAllNodes(DWGNodes);
            _treeAutoWidth.Attach(_windowSource);
            _treeAutoWidth.FitToNames();
        }

        private void NameText_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            // Full names are only needed when the screen-width cap trims them.
            if (!TreeAutoWidthController.IsTextTrimmed(sender as TextBlock))
                e.Handled = true;
        }

        private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG message, ref bool handled)
        {
            if (handled || !IsActive || message.wParam.ToInt64() != EscapeVirtualKey)
                return;

            bool isEscapeKeyMessage = message.message == WmKeyDown ||
                                      message.message == WmKeyUp ||
                                      message.message == WmSysKeyDown ||
                                      message.message == WmSysKeyUp;
            if (!isEscapeKeyMessage)
                return;

            if (message.message == WmKeyDown || message.message == WmSysKeyDown)
                HandleEscapeKey();

            // Consume both key-down and key-up before Revit can interpret Escape
            // as a command that deactivates or minimizes this modeless window.
            handled = true;
        }

        private IntPtr WindowHwndHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmSysCommand &&
                (wParam.ToInt64() & 0xFFF0) == ScMinimize &&
                !_allowExplicitMinimize)
            {
                handled = true;
                return IntPtr.Zero;
            }

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
            Dispatcher.BeginInvoke(
                new Action(() => _allowExplicitMinimize = false),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (WindowState == System.Windows.WindowState.Minimized)
                        SystemCommands.RestoreWindow(this);

                    Activate();
                }), System.Windows.Threading.DispatcherPriority.Send);
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

        private void DWGTreeView_Loaded(object sender, RoutedEventArgs e)
        {
            _treeViewControls.ExpandAllNodes(DWGNodes);
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
            FilteredDWGNodes = Search.FilterDWGNodes(DWGNodes, SearchBox.Text);
            _treeViewControls.RefreshTreeView(DWGTreeView, FilteredDWGNodes);
            SynchronizeSelectionAnchorWithFilter();
            UpdateContextSummary();
        }

        private void Window1_Closed(object sender, System.EventArgs e)
        {
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WindowHwndHook);
                _windowSource = null;
            }

            ComponentDispatcher.ThreadPreprocessMessage -= ComponentDispatcher_ThreadPreprocessMessage;
            _treeAutoWidth.Dispose();

            _themeManager.SaveThemeState();

            DWGTreeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
            DWGTreeView.Loaded -= DWGTreeView_Loaded;
            this.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown));

            _visibilityToggler?.CancelPendingRequest();
            _colorOverrideHandler?.CancelPendingRequest();
            _lineGraphicsReadHandler?.CancelPendingRequest();
            _halftoneHandler?.CancelPendingRequest();
            _applyToViewsHandler?.CancelPendingRequest();
            _windowCoordinator?.Dispose();
            _externalEvent?.Dispose();
            _colorOverrideEvent?.Dispose();
            _lineGraphicsReadEvent?.Dispose();
            _halftoneEvent?.Dispose();
            _applyToViewsEvent?.Dispose();
            _operationStatus?.Dispose();
            _themeManager.ThemeResourcesChanged -= ThemeManager_ThemeResourcesChanged;
            _themeManager.Dispose();
            _visibilityToggler.DWGNodes = null;
            _visibilityToggler.Document = null;
            _visibilityToggler.CurrentView = null;

            DWGNodes?.Clear();
            FilteredDWGNodes?.Clear();
            DWGTreeView.ItemsSource = null;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => SearchBox.Text = string.Empty;

        private void ConfigureLayerFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            OpenLayerSelectionFilters();
        }

        private void OpenLayerSelectionFilters()
        {
            _layerSelectionFiltersWindow = _windowCoordinator.ShowOrActivate(
                "layer-selection-filters",
                () => new LayerSelectionFiltersWindow(
                    _themeManager,
                    _layerSelectionFilters,
                    GetActiveViewLayerVisibility),
                window =>
                {
                    window.FiltersSaved += LayerSelectionFiltersWindow_FiltersSaved;
                    window.Closed += LayerSelectionFiltersWindow_Closed;
                });
        }

        /// <summary>
        /// Reads layer visibility from the unfiltered tree, which mirrors the
        /// active view after each refresh.
        /// </summary>
        private IReadOnlyList<DwgLayerVisibility> GetActiveViewLayerVisibility()
        {
            return (DWGNodes ?? new List<DWGNode>())
                .Where(dwgNode => dwgNode?.Layers != null)
                .Select(dwgNode => new DwgLayerVisibility(
                    dwgNode.Name,
                    dwgNode.Layers.Where(layer => !layer.IsChecked).Select(layer => layer.Name),
                    dwgNode.Layers.Where(layer => layer.IsChecked).Select(layer => layer.Name)))
                .ToList();
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settingsWindow = _windowCoordinator.ShowOrActivate(
                    "settings",
                    () => new SettingsWindow(
                        _themeManager,
                        _themeManager.IsDarkMode,
                        _commandButtons.GetLayerToggleFolder(),
                        _commandButtons.IsUsingCustomLayerToggleFolder()),
                    window =>
                    {
                        window.ThemeSettingChanged += SettingsWindow_ThemeSettingChanged;
                        window.SelectionFiltersRequested += SettingsWindow_SelectionFiltersRequested;
                        window.StorageLocationRequested += SettingsWindow_StorageLocationRequested;
                        window.Closed += SettingsWindow_Closed;
                    });
            }
            catch (Exception ex)
            {
                ShowStatus($"Settings could not be opened: {ex.Message}", true, false);
            }
        }

        private void SettingsWindow_ThemeSettingChanged(object sender, ThemeSettingChangedEventArgs e)
        {
            _themeManager.IsDarkMode = e.IsDarkMode;
            _themeManager.LoadTheme();
            _windowCoordinator.ApplyToOpenWindows(_themeManager.LoadTheme);
            _settingsWindow?.UpdateThemeState(_themeManager.IsDarkMode);
            _themeManager.SaveThemeState();

            if (_themeManager.IsHighContrastActive)
                ShowStatus("Windows High Contrast is active. CAD Manager will keep using the system contrast palette.", false, true);
        }

        private void SettingsWindow_SelectionFiltersRequested(object sender, EventArgs e)
        {
            OpenLayerSelectionFilters();
        }

        private void SettingsWindow_StorageLocationRequested(
            object sender,
            StorageLocationRequestedEventArgs e)
        {
            try
            {
                string customFolder = null;
                string destinationFolder;
                if (e.UseDefault)
                {
                    destinationFolder = _commandButtons.GetDefaultLayerToggleFolder();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(e.Folder))
                        throw new InvalidOperationException("Enter a folder path first.");

                    customFolder = Path.GetFullPath(
                        Environment.ExpandEnvironmentVariables(e.Folder.Trim()));
                    Directory.CreateDirectory(customFolder);
                    destinationFolder = customFolder;
                }

                string currentFolder = _commandButtons.GetLayerToggleFolder();
                bool folderChanged = !PathsEqual(currentFolder, destinationFolder);
                int savedPresetCount = _commandButtons.GetSavedLayerToggleCount();

                if (folderChanged && savedPresetCount > 0)
                {
                    string noun = savedPresetCount == 1 ? "file" : "files";
                    UniversalPopupWindow.Show(
                        $"Move the {savedPresetCount} existing layer toggle {noun} for this project to the new location?\n\n" +
                        "Choose No to use the new location without moving the existing files.",
                        "Move existing layer toggles?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        (Window)_settingsWindow ?? this,
                        result => ApplyLayerToggleLocation(
                            customFolder,
                            destinationFolder,
                            result == MessageBoxResult.Yes));
                    return;
                }

                ApplyLayerToggleLocation(customFolder, destinationFolder, false);
            }
            catch (Exception ex)
            {
                _settingsWindow?.SetStorageStatus(ex.Message, true);
                ShowStatus($"Layer toggle location could not be changed: {ex.Message}", true, false);
            }
        }

        private void ApplyLayerToggleLocation(
            string customFolder,
            string destinationFolder,
            bool moveExistingFiles)
        {
            try
            {
                int movedFileCount = _commandButtons.ConfigureLayerToggleFolder(
                    customFolder,
                    moveExistingFiles);
                bool isCustom = !string.IsNullOrWhiteSpace(customFolder);
                _settingsWindow?.UpdateStorageLocation(destinationFolder, isCustom);

                string message = movedFileCount > 0
                    ? $"Layer toggle location updated and {movedFileCount} existing file{(movedFileCount == 1 ? string.Empty : "s")} moved."
                    : "Layer toggle location updated.";
                _settingsWindow?.SetStorageStatus(message, false);
                ShowStatus(message, false, true);
            }
            catch (Exception ex)
            {
                _settingsWindow?.SetStorageStatus(ex.Message, true);
                ShowStatus($"Layer toggle location could not be changed: {ex.Message}", true, false);
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;

            string normalizedFirst = Path.GetFullPath(first)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedSecond = Path.GetFullPath(second)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            if (sender is SettingsWindow window)
            {
                window.ThemeSettingChanged -= SettingsWindow_ThemeSettingChanged;
                window.SelectionFiltersRequested -= SettingsWindow_SelectionFiltersRequested;
                window.StorageLocationRequested -= SettingsWindow_StorageLocationRequested;
                window.Closed -= SettingsWindow_Closed;
            }

            _settingsWindow = null;
        }

        private void LayerSelectionFiltersWindow_FiltersSaved(
            object sender,
            LayerSelectionFiltersSavedEventArgs e)
        {
            List<LayerSelectionFilterRule> filters = e.Filters.ToList();
            _layerSelectionFilterStore.Save(filters);
            _layerSelectionFilters = filters;

            int enabledCount = filters.Count(filter =>
                filter.IsEnabled && !string.IsNullOrWhiteSpace(filter.Pattern));
            string noun = enabledCount == 1 ? "filter" : "filters";
            ShowStatus($"Saved {enabledCount} enabled layer selection {noun}.", false, true);
        }

        private void LayerSelectionFiltersWindow_Closed(object sender, EventArgs e)
        {
            if (sender is LayerSelectionFiltersWindow window)
            {
                window.FiltersSaved -= LayerSelectionFiltersWindow_FiltersSaved;
                window.Closed -= LayerSelectionFiltersWindow_Closed;
            }

            _layerSelectionFiltersWindow = null;
        }

        private void ApplyLayerFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            List<LayerSelectionFilterRule> activeRules = (_layerSelectionFilters ??
                    new List<LayerSelectionFilterRule>())
                .Where(rule => rule != null &&
                               rule.IsEnabled &&
                               !string.IsNullOrWhiteSpace(rule.Pattern))
                .ToList();

            if (activeRules.Count == 0)
            {
                ShowStatus("Set up at least one enabled layer selection filter first.", true, false);
                return;
            }

            _selectionController.Clear();
            int matchCount = 0;
            foreach (DWGNode dwgNode in FilteredDWGNodes ?? Enumerable.Empty<DWGNode>())
            {
                bool parentHasMatch = false;
                foreach (LayerNode layerNode in dwgNode.Layers ?? new List<LayerNode>())
                {
                    if (!LayerSelectionFilterMatcher.IsMatch(layerNode.Name, activeRules))
                        continue;

                    layerNode.IsSelected = true;
                    SyncFilteredLayerToOriginal(layerNode);
                    parentHasMatch = true;
                    matchCount++;
                }

                if (parentHasMatch)
                    dwgNode.IsExpanded = true;
            }

            SynchronizeSelectionAnchorWithFilter();
            UpdateContextSummary();
            DWGTreeView.UpdateLayout();
            ClearNativeTreeSelection(DWGTreeView);

            string layerNoun = matchCount == 1 ? "layer row" : "layer rows";
            ShowStatus(
                matchCount == 0
                    ? "No currently listed layer rows match the saved filters."
                    : $"Selected {matchCount} matching {layerNoun}.",
                false,
                true);
        }

        private static void ClearNativeTreeSelection(ItemsControl parent)
        {
            if (parent == null)
                return;

            foreach (object item in parent.Items)
            {
                TreeViewItem container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container == null)
                    continue;

                container.IsSelected = false;
                ClearNativeTreeSelection(container);
            }
        }

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

        private void RefreshTreeView()
        {
            _treeViewControls.RefreshTreeView(DWGTreeView, FilteredDWGNodes);
            BuildLayerParentLookup();
            UpdateContextSummary();
            _treeAutoWidth?.ScheduleFit();
        }

        public void ReplaceDWGNodes(List<DWGNode> updatedNodes)
        {
            DWGNodes = updatedNodes ?? new List<DWGNode>();
            FilteredDWGNodes = Search.FilterDWGNodes(DWGNodes, SearchBox.Text);
            SynchronizeSelectionAnchorWithFilter();
            RefreshTreeView();
        }

        public void ApplyPresetStateToUi(IReadOnlyList<PresetDwgState> states)
        {
            _suppressTreeOperationEvents = true;
            try
            {
                foreach (DWGNode dwgNode in DWGNodes ?? new List<DWGNode>())
                {
                    PresetDwgState state = states?.FirstOrDefault(candidate =>
                        candidate != null &&
                        ((candidate.ImportInstanceId != null && dwgNode.ElementId != null &&
                          candidate.ImportInstanceId.GetIdValue() == dwgNode.ElementId.GetIdValue()) ||
                         string.Equals(candidate.Name, dwgNode.Name, StringComparison.CurrentCultureIgnoreCase)));
                    if (state == null)
                        continue;

                    dwgNode.IsChecked = state.IsVisible;
                    dwgNode.IsHalftone = state.IsHalftone;
                    dwgNode.LinePattern = state.LinePattern;
                    dwgNode.LineColor = state.LineColor;
                    dwgNode.LineWeight = state.LineWeight;
                    dwgNode.IsVisibilityPending = false;
                    dwgNode.IsHalftonePending = false;
                    dwgNode.OperationError = null;

                    foreach (LayerNode layerNode in dwgNode.Layers ?? new List<LayerNode>())
                    {
                        PresetLayerState layerState = state.Layers.FirstOrDefault(candidate =>
                            string.Equals(candidate.Name, layerNode.Name, StringComparison.CurrentCultureIgnoreCase));
                        if (layerState == null)
                            continue;

                        layerNode.IsChecked = layerState.IsVisible;
                        layerNode.LinePattern = layerState.LinePattern;
                        layerNode.LineColor = layerState.LineColor;
                        layerNode.LineWeight = layerState.LineWeight;
                        layerNode.IsVisibilityPending = false;
                        layerNode.OperationError = null;
                    }
                }
            }
            finally
            {
                _suppressTreeOperationEvents = false;
            }

            _treeViewControls.SortDWGs(DWGNodes);
            FilteredDWGNodes = Search.FilterDWGNodes(DWGNodes, SearchBox.Text);
            SynchronizeSelectionAnchorWithFilter();
            RefreshTreeView();
        }
        
        private bool EnsureTreeOperationsIdle()
        {
            if (!_visibilityToggler.HasPendingRequest && !_halftoneHandler.HasPendingRequest)
                return true;

            ShowStatus("Wait for the current visibility update to finish, then try again.", false, true);
            return false;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureTreeOperationsIdle())
                _commandButtons.LoadButton_Click(sender, e);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureTreeOperationsIdle())
                _commandButtons.SaveButton_Click(sender, e);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureTreeOperationsIdle())
                _commandButtons.BrowseButton_Click(sender, e);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (EnsureTreeOperationsIdle())
                _commandButtons.RefreshButton_Click(sender, e);
        }

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
                () => new ApplyToViewsWindow(
                    CollectApplyToViewsOptions(document, sourceView),
                    sourceView.Id.GetIdValue(),
                    sourceView.Name),
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

            Document document = _uiDoc?.Document;
            ElementId sourceViewId = ElementIdExtensions.FromIdValue(e.SourceViewId);
            List<ElementId> targetViewIds = e.TargetViewIds
                .Select(ElementIdExtensions.FromIdValue)
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .ToList();

            bool submitted = _applyToViewsHandler.TrySubmit(
                document,
                sourceViewId,
                targetViewIds,
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

        private static IReadOnlyList<ViewDescriptor> CollectApplyToViewsOptions(
            Document document,
            View currentView)
        {
            if (document == null || currentView == null)
                return new List<ViewDescriptor>();

            return new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(view => view.Id.GetIdValue() != currentView.Id.GetIdValue() &&
                               !view.IsTemplate &&
                               view.ViewType == ViewType.FloorPlan)
                .Select(view => new ViewDescriptor(
                    view.Id.GetIdValue(),
                    view.Name,
                    GetViewTypeName(view.ViewType)))
                .ToList();
        }

        private static string GetViewTypeName(ViewType viewType)
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

        private void ApplyToViewsCompleted(string report)
        {
            _applyToViewsPending = false;
            _applyToViewsWindow?.SetRequestState(false, report, false, true);
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
            _operationStatus.Show(message, isError, autoDismiss);
        }

        private void RenderOperationStatus(OperationStatusState status)
        {
            if (StatusText == null || status == null)
                return;

            StatusText.Text = status.DisplayText;
            string brushKey = status.IsError ? "ErrorBrush" : "ForegroundBrush";
            if (TryFindResource(brushKey) != null)
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            else
                StatusText.Foreground = SystemColors.WindowTextBrush;
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                StatusText,
                status.IsError
                    ? System.Windows.Automation.AutomationLiveSetting.Assertive
                    : System.Windows.Automation.AutomationLiveSetting.Polite);
            StatusIcon.Visibility = status.IsError
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            StatusDismissButton.Visibility = status.IsDismissible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            StatusBorder.Visibility = System.Windows.Visibility.Visible;
        }

        private void StatusDismissButton_Click(object sender, RoutedEventArgs e)
        {
            _operationStatus.Dismiss();
            DWGTreeView.Focus();
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
                string message = "Another graphics state request is already pending.";
                window.SetRequestState(false, message, true);
                ShowStatus(message, true, false);
                return;
            }

            ExternalEventRequest raiseResult = _lineGraphicsReadEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _lineGraphicsReadHandler.CancelPendingRequest();
                string message = "Revit could not queue the graphics state request. Try again.";
                window.SetRequestState(false, message, true);
                ShowStatus(message, true, false);
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
                string message = "Another line graphics update is already pending.";
                _lineGraphicsWindow.SetRequestState(false, message, true);
                ShowStatus(message, true, false);
                return;
            }

            _lineGraphicsWindow.SetRequestState(true, e.ClearOverrides
                ? "Clearing overrides…"
                : "Applying line graphics…");

            ExternalEventRequest raiseResult = _colorOverrideEvent.Raise();
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _colorOverrideHandler.CancelPendingRequest();
                string message = "Revit could not queue the graphics update. Try again.";
                _lineGraphicsWindow.SetRequestState(false, message, true);
                ShowStatus(message, true, false);
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
            _lineGraphicsReadHandler.CancelPendingRequest();
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
            View currentView = _visibilityToggler?.CurrentView ?? _uiDoc?.Document?.ActiveView;
            string viewName = currentView != null && !string.IsNullOrWhiteSpace(currentView.Name)
                ? currentView.Name
                : "No active view";
            _operationStatus.UpdateContext(viewName, _selectionController.SelectedCount);
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
                _selectionController.SelectGroup(dwgNode, IsCtrlPressed, IsShiftPressed);
                UpdateContextSummary();
                e.Handled = true;
            }
            else if (clickedItem?.DataContext is LayerNode layerNode)
            {
                _selectionController.SelectItem(layerNode, IsCtrlPressed, IsShiftPressed);
                UpdateContextSummary();
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
            _selectionController.Clear();
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
                if (_selectionController.SelectAnchoredGroupItems())
                {
                    UpdateContextSummary();
                    e.Handled = true;
                }
            }
        }

        private void SynchronizeSelectionAnchorWithFilter()
        {
            _selectionController.SynchronizeAnchorWithVisibleItems();
        }

        private static bool AreSameDwg(DWGNode left, DWGNode right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left?.ElementId == null || right?.ElementId == null)
                return false;

            return left.ElementId.GetIdValue() == right.ElementId.GetIdValue();
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
