# Revit-free UI testing survey

## Build and runtime

- The add-in targets .NET Framework 4.8, x64, WPF, and C# 7.3-compatible source.
- The checked-in project currently references the Revit 2024 `AdWindows`, `RevitAPI`, and `RevitAPIUI` DLLs from `E:\Autodesk\Revit 2024` with `Private=false`.
- Costura embeds normal managed dependencies, while the Revit assemblies remain compile-only.
- Revit 2026/.NET 8 is a declared product target but is not currently represented by a target framework in the project file.

## UI inventory and creation

- `CADManagerWindow` is created modelessly by `CADManagerCommand`. It owns the main DWG/layer tree, search, selection, status, presets, theme state, and all external-event coordination.
- `ApplyToViewsWindow` is created modelessly through `WindowCoordinator` from the main window.
- `LineGraphicsWindow` is created modelessly through `WindowCoordinator` from the main window.
- `UniversalPopupWindow` is a modeless fallback notification window.
- There are no active WPF user controls and no conventional DataContext-based view models. `CommandButtons`, `TreeViewControls`, and the controller classes are code-behind collaborators rather than presentation view models.

## Revit API boundary

- `CADManagerWindow` directly owns `UIDocument`, `Document`, `View`, `ElementId`, `ExternalEvent`, and each external-event handler.
- `ApplyToViewsWindow` currently reads `Document`/`View` data directly; this is the smallest safe UI seam to remove.
- `DWGNode`, `TreeOperationTargets`, and `LineGraphicsTarget` contain `ElementId` values.
- `CommandButtons`, `LayerVisibilityManager`, `DWGDataService`, `DWGVisibilityController`, and `ColorOverrideController` use Revit types.
- Model writes are correctly isolated in transactions inside `ApplyToViewsHandler`, `VisibilityToggler`, `HalftoneHandler`, `ColorOverrideHandler`, and the legacy controllers.
- Line Graphics presentation state (`LineGraphicsSnapshot`, colors, patterns, weights) is otherwise plain managed data.

## Request and result flow

- Visibility, halftone, Apply to Views, graphics reads, and graphics writes use guarded immutable requests followed by `ExternalEvent.Raise()`.
- Handlers validate the active document/view, transact, then invoke success/error callbacks on Revit's UI thread.
- Secondary windows raise request events to the main window; the main window queues Revit work and returns status through public window methods.

## Dialogs and persistence

- Preset browsing uses `Microsoft.Win32.OpenFileDialog`.
- Line Graphics color selection uses `System.Windows.Forms.ColorDialog`.
- Startup failures use Revit `TaskDialog`; routine fallback notifications use `UniversalPopupWindow`.
- Presets are JSON files beside the model or under the user's roaming AppData fallback.
- Theme/window state is JSON under common application data. This path needs an injectable override for repeatable harness runs.

## Existing tests

- No automated test projects existed before this work.
- `docs/manual-revit-ui-smoke-test.md` is the current live-Revit validation checklist.

## Minimal behaviour-preserving refactor

1. Add a Revit-free core project containing view descriptors and selection/filter state. The same C# 7.3 sources are linked into the WPF add-in because its temporary XAML compilation project does not reliably propagate project references.
2. Change `ApplyToViewsWindow` to accept those descriptors and expose numeric IDs, leaving conversion to `ElementId` at the main-window boundary.
3. Add an injectable theme-settings path without changing the production default.
4. Add multi-target xUnit tests for the core selection model.
5. Add an STA WPF harness that opens the real Apply to Views, Line Graphics, and popup windows, drives controls, records fake-host requests, and captures light/dark screenshots.

## Risks and deliberate limits

- Refactoring the entire main window behind a new host interface would touch every Revit workflow and is not the smallest safe first step. The initial harness therefore covers the real secondary windows and shared resources; the main Revit coordinator remains on the manual smoke-test list.
- Apply to Views changes its presentation contract from `ElementId` to `long`. Conversion remains at the existing Revit boundary and requires explicit compatibility handling for older ElementId constructors.
- Native file/color dialogs remain manual because invoking them would block automation. Their surrounding behavior should move behind an injectable dialog service in a later focused change.
- Code inside external-event handlers still requires live Revit validation.
