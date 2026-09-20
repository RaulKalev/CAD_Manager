using CAD_Manager.Core;
using CAD_Manager.Models;
using CAD_Manager.Services;
using CAD_Manager.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CAD_Manager.UiHarness
{
    internal static class Program
    {
        private static readonly List<string> Results = new List<string>();
        private static int _failures;
        private static string _outputDirectory;
        private static string _galleryDirectory;

        [STAThread]
        private static int Main()
        {
            _outputDirectory = Environment.GetEnvironmentVariable("CAD_MANAGER_UI_TEST_OUT");
            if (string.IsNullOrWhiteSpace(_outputDirectory))
            {
                _outputDirectory = Path.Combine(
                    Environment.CurrentDirectory,
                    "Tests",
                    "UiHarness",
                    "out");
            }
            _galleryDirectory = Path.Combine(_outputDirectory, "gallery");
            Directory.CreateDirectory(_galleryDirectory);

            string settingsPath = Path.Combine(_outputDirectory, "theme-test.json");
            if (File.Exists(settingsPath))
                File.Delete(settingsPath);

            Application application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            application.Startup += (sender, args) => application.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    try
                    {
                        RunApplyToViewsScenarios(settingsPath);
                        RunLineGraphicsScenarios(settingsPath);
                        RunLayerSelectionFilterScenarios(settingsPath);
                        RunSettingsScenarios(settingsPath);
                        RunPopupScenario(settingsPath);
                    }
                    catch (Exception ex)
                    {
                        Check(false, "Unhandled harness exception: " + ex);
                    }
                    finally
                    {
                        File.WriteAllLines(Path.Combine(_outputDirectory, "ui-harness.log"), Results);
                        application.Shutdown(_failures);
                    }
                }));

            return application.Run();
        }

        private static void RunApplyToViewsScenarios(string settingsPath)
        {
            IReadOnlyList<ViewDescriptor> views = CreateViews();
            FakeHost host = new FakeHost();
            ApplyToViewsWindow window = new ApplyToViewsWindow(views, 1, "Level 1 – Source")
            {
                Width = 500,
                Height = 600,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40
            };
            ThemeManager theme = new ThemeManager(window, null, settingsPath) { IsDarkMode = true };
            theme.LoadTheme();
            host.Attach(window);
            window.Show();
            Pump();

            DataGrid grid = Find<DataGrid>(window, "ViewsDataGrid");
            TextBox search = Find<TextBox>(window, "ViewSearchBox");
            TextBlock summary = Find<TextBlock>(window, "SelectionSummaryText");
            Button apply = Find<Button>(window, "ApplyButton");
            TextBlock status = Find<TextBlock>(window, "ResultStatusText");

            Check(grid.Items.Count == 4, "Apply to Views loads four fake views; actual=" + grid.Items.Count);
            Check(grid.SelectedItems.Count == 0, "Apply to Views starts with no selection");
            Check(!apply.IsEnabled, "Apply is disabled with no selection");

            grid.SelectedItems.Add(grid.Items[1]);
            grid.SelectedItems.Add(grid.Items[3]);
            Pump();
            Check(summary.Text.Contains("2 views selected"), "Selection summary reports two views; text=" + summary.Text);
            Check(apply.IsEnabled, "Apply is enabled for a multi-selection");

            search.Text = "Level 2";
            Pump();
            Check(grid.Items.Count == 1, "Search filters to one visible view; actual=" + grid.Items.Count);
            Check(summary.Text.Contains("2 views selected") && summary.Text.Contains("shown"),
                "Hidden selections survive filtering; text=" + summary.Text);

            search.Text = string.Empty;
            Pump();
            Click(apply);
            Pump();
            Check(host.Log.Count(entry => entry.StartsWith("APPLY_VIEWS", StringComparison.Ordinal)) == 1,
                "Apply emits one fake-host request");
            Check(status.Text.Contains("Applied settings to 2 views"), "Completion returns to the real status host; text=" + status.Text);

            Capture(window, "apply_to_views_selected", "dark");
            theme.IsDarkMode = false;
            theme.LoadTheme();
            Pump();
            Capture(window, "apply_to_views_selected", "light");

            search.Text = "no matching view";
            Pump();
            Check(Find<TextBlock>(window, "EmptyStateText").Visibility == Visibility.Visible,
                "No-results empty state is visible");
            Capture(window, "apply_to_views_empty", "light");

            window.Close();
            theme.Dispose();
            Pump();
        }

        private static void RunLineGraphicsScenarios(string settingsPath)
        {
            AutoDialogs dialogs = new AutoDialogs();
            FakeHost host = new FakeHost();
            LineGraphicsWindow window = new LineGraphicsWindow(null, dialogs)
            {
                Width = 500,
                Height = 560,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 80,
                Top = 80
            };
            ThemeManager theme = new ThemeManager(window, null, settingsPath) { IsDarkMode = true };
            theme.LoadTheme();
            host.Attach(window);
            window.Show();
            Pump();

            bool began = window.TryBeginLoad("3 layers in 2 DWGs");
            window.LoadSnapshot(new LineGraphicsSnapshot(
                new[] { "Solid", "Dash", "Center" },
                null,
                null,
                null,
                true,
                true,
                true));
            Pump();

            ComboBox pattern = Find<ComboBox>(window, "PatternComboBox");
            ComboBox weight = Find<ComboBox>(window, "WeightComboBox");
            Button color = Find<Button>(window, "ColorButton");
            Button apply = Find<Button>(window, "ApplyButton");
            Button clear = Find<Button>(window, "ClearButton");
            TextBlock colorText = Find<TextBlock>(window, "ColorText");

            Check(began, "Line Graphics begins an asynchronous snapshot load");
            Check(pattern.SelectedItem != null && pattern.SelectedItem.ToString().Contains("Mixed values"),
                "Pattern displays a user-facing mixed value; value=" + pattern.SelectedItem);
            Check(weight.SelectedItem != null && weight.SelectedItem.ToString().Contains("Mixed values"),
                "Weight displays a user-facing mixed value; value=" + weight.SelectedItem);
            Check(!apply.IsEnabled, "Line Graphics Apply starts disabled until an edit is staged");

            pattern.SelectedIndex = 2;
            weight.SelectedIndex = 6;
            string colorBeforeCancel = colorText.Text;
            dialogs.AcceptColor = false;
            Click(color);
            Pump();
            Check(colorText.Text == colorBeforeCancel, "Canceling AutoDialogs preserves the current color text");

            dialogs.AcceptColor = true;
            Click(color);
            Pump();
            Check(dialogs.Transcript.Count == 2, "Color picker and cancel path are routed through AutoDialogs");
            Check(colorText.Text == "RGB 24, 122, 255", "Automatic color result updates the real UI; text=" + colorText.Text);
            Check(apply.IsEnabled, "Line Graphics Apply enables after edits");

            Click(apply);
            Pump();
            Check(host.Log.Any(entry => entry.StartsWith("APPLY_GRAPHICS clear=False", StringComparison.Ordinal)),
                "Line Graphics emits a fake-host apply request");
            Check(!apply.IsEnabled, "Line Graphics Apply disables after completion");

            Capture(window, "line_graphics_applied", "dark");
            theme.IsDarkMode = false;
            theme.LoadTheme();
            Pump();
            Capture(window, "line_graphics_applied", "light");

            Click(clear);
            Pump();
            Check(host.Log.Any(entry => entry.StartsWith("APPLY_GRAPHICS clear=True", StringComparison.Ordinal)),
                "Clear Overrides emits a distinct fake-host request");

            window.SetRequestState(false, "The active view changed. Refresh and try again.", true);
            Pump();
            Capture(window, "line_graphics_error", "light");

            window.Close();
            theme.Dispose();
            Pump();
        }

        private static void RunPopupScenario(string settingsPath)
        {
            UniversalPopupWindow window = new UniversalPopupWindow
            {
                Width = 460,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 120,
                Top = 120
            };
            ThemeManager theme = new ThemeManager(window, null, settingsPath) { IsDarkMode = true };
            theme.LoadTheme();
            Find<TextBlock>(window, "TitleText").Text = "Automation warning";
            Find<TextBlock>(window, "MessageText").Text = "A deliberately long message verifies wrapping, spacing, and contrast without opening a modal dialog.";
            Button ok = Find<Button>(window, "Button3");
            ok.Content = "_OK";
            ok.Visibility = Visibility.Visible;
            window.Show();
            Pump();
            Capture(window, "notification", "dark");

            theme.IsDarkMode = false;
            theme.LoadTheme();
            Pump();
            Capture(window, "notification", "light");
            Check(window.IsVisible, "Universal popup is modeless in the harness");

            bool confirmed = false;
            UniversalPopupWindow.Show(
                "Move the existing project toggles?",
                "Move test",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                window,
                result => confirmed = result == MessageBoxResult.Yes);
            Pump();
            UniversalPopupWindow questionWindow = Application.Current.Windows
                .OfType<UniversalPopupWindow>()
                .First(candidate => !ReferenceEquals(candidate, window) && candidate.Title == "Move test");
            Click(Find<Button>(questionWindow, "Button3"));
            Pump();
            Check(confirmed && !questionWindow.IsVisible, "Modeless confirmation returns the selected answer");

            window.Close();
            theme.Dispose();
            Pump();
        }

        private static void RunLayerSelectionFilterScenarios(string settingsPath)
        {
            List<LayerSelectionFilterRule> initialRules = new List<LayerSelectionFilterRule>
            {
                new LayerSelectionFilterRule
                {
                    IsEnabled = true,
                    MatchType = LayerNameMatchType.Contains,
                    Pattern = "WALL"
                },
                new LayerSelectionFilterRule
                {
                    IsEnabled = false,
                    MatchType = LayerNameMatchType.StartsWith,
                    Pattern = "A-"
                }
            };

            string filterStorePath = Path.Combine(_outputDirectory, "layer-selection-filters-test.json");
            if (File.Exists(filterStorePath))
                File.Delete(filterStorePath);
            LayerSelectionFilterStore filterStore = new LayerSelectionFilterStore(filterStorePath);
            filterStore.Save(initialRules);
            IReadOnlyList<LayerSelectionFilterRule> loadedRules = filterStore.Load();
            Check(
                loadedRules.Count == 2 &&
                loadedRules[1].MatchType == LayerNameMatchType.StartsWith &&
                loadedRules[1].Pattern == "A-",
                "Layer filter definitions persist with their operators and enabled state");

            LayerSelectionFiltersWindow window = new LayerSelectionFiltersWindow(null, initialRules)
            {
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100
            };
            ThemeManager theme = new ThemeManager(window, null, settingsPath) { IsDarkMode = true };
            theme.LoadTheme();
            IReadOnlyList<LayerSelectionFilterRule> savedRules = null;
            window.FiltersSaved += (sender, args) => savedRules = args.Filters;
            window.Show();
            Pump();

            DataGrid rulesGrid = Find<DataGrid>(window, "RulesDataGrid");
            Button addRule = Find<Button>(window, "AddRuleButton");
            Button cancelFilters = Find<Button>(window, "CancelFiltersButton");
            Button saveFilters = Find<Button>(window, "SaveFiltersButton");

            Check(rulesGrid.Items.Count == 2, "Layer filters load existing rules; actual=" + rulesGrid.Items.Count);
            Check(window.IsVisible, "Layer filter editor is modeless in the harness");
            Check(
                Math.Abs(addRule.ActualHeight - cancelFilters.ActualHeight) < 0.1 &&
                Math.Abs(cancelFilters.ActualHeight - saveFilters.ActualHeight) < 0.1,
                "Layer filter footer actions share one aligned height");
            CheckBox enabledCheckBox = FindVisualDescendant<CheckBox>(rulesGrid);
            ComboBox inlineMatchEditor = FindVisualDescendant<ComboBox>(rulesGrid);
            TextBox inlinePatternEditor = FindVisualDescendant<TextBox>(rulesGrid);
            Check(
                enabledCheckBox != null && inlineMatchEditor != null && inlinePatternEditor != null,
                "Layer filter controls are live without entering DataGrid edit mode");
            bool previousEnabledState = initialRules[0].IsEnabled;
            enabledCheckBox.IsChecked = !enabledCheckBox.IsChecked;
            Pump();
            Check(
                ((LayerSelectionFilterRule)rulesGrid.Items[0]).IsEnabled != previousEnabledState,
                "Layer filter checkbox updates its rule immediately");
            enabledCheckBox.IsChecked = true;
            Pump();
            Capture(window, "layer_selection_filters", "dark");

            rulesGrid.SelectedItem = rulesGrid.Items[0];
            rulesGrid.CurrentCell = new DataGridCellInfo(rulesGrid.Items[0], rulesGrid.Columns[1]);
            rulesGrid.BeginEdit();
            Pump();
            ComboBox matchEditor = FindVisualDescendant<ComboBox>(rulesGrid);
            Check(
                matchEditor != null && matchEditor.Items.Cast<object>().All(item =>
                    item.ToString() == "Equals" ||
                    item.ToString() == "Contains" ||
                    item.ToString() == "Starts with" ||
                    item.ToString() == "Ends with"),
                "Match editor exposes user-facing operator labels");
            rulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);

            rulesGrid.CurrentCell = new DataGridCellInfo(rulesGrid.Items[0], rulesGrid.Columns[2]);
            rulesGrid.BeginEdit();
            Pump();
            TextBox patternEditor = FindVisualDescendant<TextBox>(rulesGrid);
            Brush expectedEditorBackground = window.TryFindResource("ControlSurfaceBrush") as Brush;
            Check(
                patternEditor != null &&
                expectedEditorBackground != null &&
                patternEditor.Background != null &&
                string.Equals(
                    patternEditor.Background.ToString(),
                    expectedEditorBackground.ToString(),
                    StringComparison.OrdinalIgnoreCase),
                "Layer-name editor uses the active theme background");
            Capture(window, "layer_selection_filters_editing", "dark");
            rulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            rulesGrid.UnselectAll();

            theme.IsDarkMode = false;
            theme.LoadTheme();
            Pump();
            Capture(window, "layer_selection_filters", "light");

            Click(addRule);
            Pump();
            Check(rulesGrid.Items.Count == 3, "Add rule appends an editable filter row");

            LayerSelectionFilterRule addedRule = rulesGrid.Items[2] as LayerSelectionFilterRule;
            addedRule.Pattern = "-DEMO";
            addedRule.MatchType = LayerNameMatchType.EndsWith;
            Click(saveFilters);
            Pump();
            Check(savedRules != null && savedRules.Count == 3, "Save emits all non-empty filter rules");

            theme.Dispose();
            Pump();
        }

        private static void RunSettingsScenarios(string settingsPath)
        {
            string locationStorePath = Path.Combine(_outputDirectory, "layer-toggle-locations-test.json");
            if (File.Exists(locationStorePath))
                File.Delete(locationStorePath);

            LayerToggleLocationStore locationStore = new LayerToggleLocationStore(locationStorePath);
            locationStore.Set("PROJECT-A", Path.Combine(_outputDirectory, "custom-toggles"));
            Check(
                locationStore.Get("PROJECT-A").EndsWith("custom-toggles", StringComparison.OrdinalIgnoreCase),
                "Custom layer-toggle locations persist per project");
            locationStore.Remove("PROJECT-A");
            Check(locationStore.Get("PROJECT-A") == null, "Project layer-toggle location can return to default");

            string migrationRoot = Path.Combine(_outputDirectory, "toggle-migration-test");
            string sourceFolder = Path.Combine(migrationRoot, "source");
            string destinationFolder = Path.Combine(migrationRoot, "destination");
            if (Directory.Exists(migrationRoot))
                Directory.Delete(migrationRoot, true);
            Directory.CreateDirectory(sourceFolder);
            File.WriteAllText(Path.Combine(sourceFolder, "A.json"), "{}");
            File.WriteAllText(Path.Combine(sourceFolder, "notes.txt"), "keep");
            int movedCount = LayerToggleLocationStore.MovePresetFiles(sourceFolder, destinationFolder);
            Check(
                movedCount == 1 &&
                File.Exists(Path.Combine(destinationFolder, "A.json")) &&
                !File.Exists(Path.Combine(sourceFolder, "A.json")) &&
                File.Exists(Path.Combine(sourceFolder, "notes.txt")),
                "Changing storage moves only layer-toggle JSON files after confirmation");

            string defaultFolder = Path.Combine(_outputDirectory, "project-default-toggles");
            SettingsWindow window = new SettingsWindow(
                null,
                true,
                defaultFolder,
                false)
            {
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 140,
                Top = 80
            };
            ThemeManager theme = new ThemeManager(window, null, settingsPath) { IsDarkMode = true };
            theme.LoadTheme();

            bool themeChanged = false;
            bool filtersRequested = false;
            StorageLocationRequestedEventArgs storageRequest = null;
            window.ThemeSettingChanged += (sender, args) => themeChanged = !args.IsDarkMode;
            window.SelectionFiltersRequested += (sender, args) => filtersRequested = true;
            window.StorageLocationRequested += (sender, args) => storageRequest = args;
            window.Show();
            Pump();

            Check(window.IsVisible, "Settings window is modeless in the harness");
            Button settingsCloseButton = FindVisualDescendant<Button>(window);
            Check(
                settingsCloseButton != null && Math.Abs(settingsCloseButton.ActualHeight - 40) < 0.1,
                "Caption close button fills the complete title-bar height");
            Capture(window, "settings", "dark");
            Click(Find<Button>(window, "ThemeButton"));
            Click(Find<Button>(window, "SelectionFiltersButton"));
            TextBox storagePath = Find<TextBox>(window, "StoragePathTextBox");
            storagePath.Text = Path.Combine(_outputDirectory, "chosen-toggles");
            Click(Find<Button>(window, "SaveStorageButton"));
            Pump();
            Check(themeChanged, "Settings raises an immediate theme change");
            Check(filtersRequested, "Settings opens the layer selection filter editor");
            Check(
                storageRequest != null &&
                !storageRequest.UseDefault &&
                storageRequest.Folder.EndsWith("chosen-toggles", StringComparison.OrdinalIgnoreCase),
                "Settings submits a custom project storage location");

            theme.IsDarkMode = false;
            theme.LoadTheme();
            window.UpdateThemeState(false);
            Pump();
            Capture(window, "settings", "light");

            window.Close();
            theme.Dispose();
            Pump();
        }

        private static IReadOnlyList<ViewDescriptor> CreateViews()
        {
            return new List<ViewDescriptor>
            {
                new ViewDescriptor(10, "Coordination – This is a deliberately very long view name used for clipping checks", "Floor Plan"),
                new ViewDescriptor(20, "Level 2", "Floor Plan"),
                new ViewDescriptor(30, "Level 3", "Floor Plan"),
                new ViewDescriptor(40, "Roof", "Floor Plan")
            };
        }

        private static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement
        {
            T value = root.FindName(name) as T;
            if (value == null)
                throw new InvalidOperationException($"Could not find {typeof(T).Name} named {name}.");
            return value;
        }

        private static T FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    return match;

                T descendant = FindVisualDescendant<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        private static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        private static void Pump(int rounds = 8)
        {
            for (int index = 0; index < rounds; index++)
            {
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.Background,
                    new Action(() => { }));
            }
        }

        private static void Check(bool ok, string what)
        {
            Results.Add((ok ? "PASS " : "FAIL ") + what);
            if (!ok)
                _failures++;
        }

        private static void Capture(Window window, string name, string theme)
        {
            window.UpdateLayout();
            double width = Math.Max(1, window.ActualWidth);
            double height = Math.Max(1, window.ActualHeight);
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96 * dpi.DpiScaleX,
                96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            bitmap.Render(window);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string fileName = $"{name}_{theme}_{pixelWidth}x{pixelHeight}.png";
            string path = Path.Combine(_galleryDirectory, fileName);
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);

            Check(File.Exists(path) && new FileInfo(path).Length > 0, "Captured " + fileName);
        }
    }
}
