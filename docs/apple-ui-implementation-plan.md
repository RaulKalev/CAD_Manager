# Apple-aligned UI implementation plan

## Objective

Evolve CAD Manager into a calm, responsive, predictable Windows/Revit utility using Apple's design principles—purpose, agency, responsibility, familiarity, flexibility, simplicity, craft, and delight—without imitating macOS chrome or weakening Revit API safety.

The implementation must remain:

- C# 7.3 compatible.
- WPF-based.
- Compatible with Revit 2021+ API patterns, with Revit 2024 and 2026 as primary validation targets.
- Modeless: plugin-owned windows use `Show()`, never `ShowDialog()`.
- Transaction-safe: all model writes remain behind `ExternalEvent` handlers and transactions.
- Incremental: no full MVVM rewrite and no replacement of the existing handler architecture.

## Experience target

The interface should feel calm, confident, and immediate. Users should always know:

- Which DWG, layer, or views an action will affect.
- Whether a Revit operation is pending, complete, or failed.
- How to undo or recover from a change.
- Where focus is and how to operate the UI from the keyboard.
- How to return to Revit without dismissing plugin tools.

## Current architectural boundary

The Graphify map identifies `CADManagerWindow` as the primary cross-community bridge. It currently coordinates window behavior, selection, search, dialogs, external events, theme state, and command handling. The existing responsibility-specific handlers are the correct boundary and should be preserved:

- `VisibilityToggler`
- `HalftoneHandler`
- `ColorOverrideHandler`
- `ApplyToViewsHandler`

The implementation should reduce presentation responsibilities in `CADManagerWindow` while leaving model mutations in these handlers.

## Implementation status

Code-complete for phases 1, 2, 4, 5, and 6; Revit-hosted validation remains:

- Added a single-instance `WindowCoordinator` for modeless secondary windows.
- Converted Apply to Views from `ShowDialog()`/`DialogResult` to a modeless request event.
- Changed Apply to Views targets from long-lived `View` objects to captured `ElementId` values resolved inside the external-event handler.
- Added pending-request protection so queued `ExternalEvent` data cannot be overwritten.
- Added active-document and deleted-view validation before the transaction starts.
- Added inline pending, completion, and error feedback to the Apply to Views window and main window.
- Added initial automation names and restored DataGrid cell focus visuals in Apply to Views.
- Converted Line Graphics into a single-instance modeless inspector with visible target scope.
- Added a read-only external-event handler so Line Graphics loads Revit state inside a valid API context.
- Changed line-graphics writes to immutable, guarded requests resolved inside `ColorOverrideHandler`.
- Added active-document, active-view, deleted-target, and deleted-pattern validation for Line Graphics.
- Kept Apply and Clear explicit, left the inspector open, and added inline loading/success/error feedback.
- Routed routine save/load/template notifications into the main non-blocking status host.
- Changed the fallback universal notification window to modeless and removed duplicate button subscriptions.
- Added the first semantic theme brushes for accent, error, warning, success, and disabled content.
- Added shared design-token and control-style dictionaries for spacing, radii, typography, pointer targets, focus, buttons, inputs, status surfaces, and DataGrids.
- Reworked the light and dark palettes around semantic surface, separator, selection, focus, accent, and status roles while preserving legacy brush keys.
- Applied the shared visual language to Apply to Views, Line Graphics, fallback notifications, and the main status/search areas.
- Removed decorative blur from the main surface and moved secondary tools to familiar native Windows framing.
- Replaced the translated main command icons with a real 32-DIP command strip using one shared button style.
- Propagated theme changes to every open modeless tool through `WindowCoordinator`.
- Replaced the main window's transparent custom frame and manual edge resizer with native Windows sizing and title-bar behavior.
- Removed permanent always-on-top behavior and added an explicit, persisted, accessible pin control.
- Reorganized the main window into a visible context header, labeled command bar, full-width search field, status host, and content area.
- Added a live active-view and selected-item summary without moving any Revit API write into UI code.
- Removed the conflicting custom `800`-DIP minimum height; the window now has one authoritative `500`-DIP minimum.
- Rebuilt DWG and layer rows around 32-DIP visibility, halftone, and graphics targets with visible keyboard focus and accessible action names.
- Made selection feedback span the coherent row surface and added an in-window shortcut reference for Ctrl/Shift selection, Ctrl+A, and Escape.
- Changed visibility and halftone row writes to immutable, single-pending requests validated against the active document and view.
- Added immediate row-level pending feedback, persistent inline errors, and automatic rollback to the prior displayed value when Revit rejects or fails a change.
- Stopped rebuilding the TreeView for ordinary selection/property changes, preserving realized containers, expansion, focus, and scroll position.
- Synchronized filtered DWG selection back to canonical nodes so selection counts and multi-row actions remain predictable while searching.
- Added a persistent selected-view set to Apply to Views so search filtering no longer discards hidden selections.
- Added total/visible selection summaries, an explicit dynamic “Apply to N Views” action, and an empty-search state.
- Replaced Line Graphics sentinel labels with user-facing mixed-value and no-override states that explain whether a value will change.
- Disabled Line Graphics Apply until a meaningful edit is staged and separated Clear All Overrides into a dedicated reset surface.
- Added consistent dismissible secondary-tool status surfaces and explicit Revit Undo recovery wording after line-graphics changes.
- Added accessible names and help text to the remaining icon-only, search, status, and stateful controls.
- Defined explicit tab order in the main and secondary windows, initial focus in inspectors, and focus return to the invoking control when a modeless tool closes.
- Added keyboard access keys for primary commands plus Alt+F search focus in the main window.
- Added a live Windows High Contrast theme based on system colors and propagated system contrast changes to every open modeless tool.
- Suppressed nonessential indeterminate progress motion when Windows client-area animations are disabled.
- Added keyboard-only focus adorners to shared buttons, icon toggles, inputs, ComboBoxes, DataGrids, TreeView rows, and row actions without leaving a mouse-click focus ring behind.
- Removed the remaining ComboBox popup animation so reduced-motion behavior is immediate and deterministic.
- Made routine status messages explicitly time-limited to 15 seconds while keeping errors persistent, dismissible, and assertive for assistive technology.
- Added a non-color error icon and dismiss action to the main one-line context/status row.
- Preserved tree selection and expansion through refresh, and preserved selection while the search filter changes.
- Replaced preset loading's mutable compatibility path with an immutable, single-pending external-event request validated against the active document and view; bound rows synchronize only after the Revit transaction succeeds.
- Guarded save, load, browse, and refresh while visibility or halftone changes are pending.
- Tightened modeless-window registration, cleanup, activation, and deferred focus restoration.
- Removed stale custom-title-bar project entries and eliminated the last runtime popup click subscriptions.
- Audited the core palette contrast; primary/secondary text and accent-button text now meet the plan's normal-text contrast target in light and dark themes.

Remaining validation slice:

- Run the Revit 2024 keyboard/Narrator and 100%, 150%, and 200% display-scaling matrix, including longer localized labels.
- Run the equivalent Revit 2026 matrix when Revit 2026 API/runtime assemblies are available on the test machine.
- Verify the saved-preset, document/view-switch, and request-cancellation cases in a live Revit host.

Phases 7 (optional restrained motion) and 8 (longer-term coupling reduction) remain deliberately separate from this completion pass. Motion should only be considered after the accessibility matrix passes.

## Delivery sequence

### Phase 0 — Baseline and guardrails

Purpose: preserve behavior before changing interaction structure.

Work:

- Record light and dark screenshots for the main window, apply-to-views window, line-graphics window, and notifications.
- Document keyboard behavior: Tab, Shift+Tab, Enter, Space, Escape, Ctrl+A, selection ranges, and search focus.
- Add a manual Revit smoke-test checklist for 2024 and 2026.
- Add or correct ignore rules for `.vs/`, `bin/`, `obj/`, temporary WPF projects, and generated `*.g.cs` files. Remove tracked generated artifacts in a separate, explicitly reviewed cleanup.
- Establish a rule that a UI refactor cannot move a Revit model write out of an `ExternalEvent` transaction.

Acceptance:

- Existing workflows are captured before visual changes.
- Generated artifacts no longer dominate future architectural analysis.
- Both primary Revit targets have a repeatable smoke-test script.

### Phase 1 — Modeless workflow and operation lifecycle

Principles: agency, responsibility, flexibility.

Work:

- Replace plugin-owned `ShowDialog()` calls for `ApplyToViewsWindow`, `LineGraphicsWindow`, and `UniversalPopupWindow`.
- Introduce a small `WindowCoordinator` that owns single-instance modeless secondary windows, owner relationships, activation, and cleanup.
- Change `ApplyToViewsWindow` to raise `ApplyRequested` and `CancelRequested` events. Pass selected `ElementId` values rather than holding long-lived `View` objects where practical; resolve and validate views inside `ApplyToViewsHandler.Execute`.
- Change `LineGraphicsWindow` into a modeless inspector. Expose immutable request data for Apply, Clear Overrides, and Close. Show the current scope in the window, for example “3 layers in 2 DWGs.”
- Keep each handler responsible for one operation. Add completion/failure callbacks that return presentation-safe result objects.
- Add per-operation pending state so a duplicate request cannot overwrite handler input before Revit consumes the first external event.
- Re-resolve the active document and view when an external event executes. Fail gracefully if the user changed documents, closed a view, or deleted a target while a modeless tool remained open.
- Replace routine success/info popups with non-blocking status feedback. Keep modeless warnings or confirmations only where the user must make a decision.

Primary files:

- `CAD Manager/CADManagerWindow.xaml.cs`
- `CAD Manager/UI/ApplyToViewsWindow.xaml.cs`
- `CAD Manager/UI/LineGraphicsWindow.xaml.cs`
- `CAD Manager/UI/UniversalPopupWindow.xaml.cs`
- `CAD Manager/Handlers/ApplyToViewsHandler.cs`
- `CAD Manager/Handlers/ColorOverrideHandler.cs`
- New `CAD Manager/Services/WindowCoordinator.cs`
- New operation request/result models under `CAD Manager/Models/`

Acceptance:

- No plugin-owned WPF window calls `ShowDialog()`.
- Revit remains interactive while every plugin window is open.
- Repeated clicks cannot replace pending handler data.
- Document/view changes while a secondary window is open produce a safe, understandable error.
- Every write still occurs inside the appropriate handler transaction.

### Phase 2 — Shared visual language

Principles: familiarity, simplicity, craft.

Work:

- Create shared resource dictionaries for design tokens and control styles.
- Define semantic colors rather than control-specific colors: `SurfaceBrush`, `ElevatedSurfaceBrush`, `ControlSurfaceBrush`, `SeparatorBrush`, `AccentBrush`, `DangerBrush`, `WarningBrush`, `SuccessBrush`, `DisabledForegroundBrush`, and focus brushes.
- Define spacing tokens on a 4-DIP base and common corner radii.
- Define a restrained type scale using the Windows system font: caption, body, emphasized body, section title, and window title.
- Consolidate duplicate Button, ToggleButton, TextBox, ComboBox, ToolTip, and DataGrid styles.
- Remove hard-coded gray, green, red, and blue values from views. Theme-specific values belong in light/dark dictionaries.
- Validate normal text at 4.5:1 contrast and large/icon content at 3:1 where applicable.
- Keep surfaces mostly opaque in Revit. Use tonal layering and restrained shadow instead of decorative blur or glass.

Primary files:

- New `CAD Manager/Themes/DesignTokens.xaml`
- New `CAD Manager/Themes/ControlStyles.xaml`
- `CAD Manager/Themes/LightTheme.xaml`
- `CAD Manager/Themes/DarkTheme.xaml`
- `CAD Manager/Themes/ScrollBarStyle.xaml`
- `CAD Manager/Themes/ToolTipStyle.xaml`
- All plugin XAML views

Acceptance:

- No view-level hard-coded interaction colors remain except intentionally data-driven color previews.
- Shared controls render consistently in light and dark themes.
- Tooltips and button templates have one authoritative definition.
- Theme switching does not replace unrelated window resources.

### Phase 3 — Main window hierarchy and native behavior

Principles: purpose, familiarity, simplicity.

Work:

- Replace manual resize borders with `System.Windows.Shell.WindowChrome`, preserving native sizing, DPI behavior, and Windows conventions.
- Remove permanent `Topmost=true`. If users need it, add an explicit, persisted pin control with an accessible label.
- Resolve the current minimum-height conflict between XAML (`500`) and `WindowResizer` (`800`).
- Replace the translated icon strip with a real command bar using layout columns and shared button styles.
- Group commands by purpose:
  - Presets: Save, Load Matching, Browse.
  - Scope: Apply to Views.
  - Data: Refresh.
  - Appearance: Theme and optional Pin.
- Give the primary or least obvious commands visible labels. Keep icons as supporting cues.
- Widen the search field and place it directly above the content it filters.
- Add a compact context/status row showing active view, selected item count, and pending operation state.
- Preserve a dense desktop layout, but use at least 32-DIP pointer targets and adequate spacing between destructive and routine actions.

Primary files:

- `CAD Manager/CADManagerWindow.xaml`
- `CAD Manager/CADManagerWindow.xaml.cs`
- `CAD Manager/UI/CustomTitleBar.xaml`
- `CAD Manager/UI/CustomTitleBar.xaml.cs`
- `CAD Manager/Helpers/WindowResizer.cs`
- `CAD Manager/Services/ThemeManager.cs`

Acceptance:

- The window resizes from every normal edge/corner and behaves correctly at 100–200% DPI.
- The window is not always-on-top unless the user explicitly pins it.
- No control placement depends on `TranslateTransform` offsets.
- A new user can distinguish Presets, Apply to Views, Refresh, Theme, and Pin without waiting for tooltips.

### Phase 4 — Tree interaction and direct feedback

Principles: response, direct manipulation, grouping and mapping.

Work:

- Increase row-action hit targets while keeping icons visually compact.
- Preserve individual checkbox state and current multi-selection semantics; never allow bulk actions to silently overwrite unrelated row state.
- Make the entire selected row visually coherent rather than highlighting only scattered elements.
- Keep visibility, halftone, and graphics actions adjacent to the item they affect.
- Add accessible text equivalents for Eye, Halftone, and Graphics controls.
- Show pending state on the affected rows while an external event is queued.
- If an operation fails, restore the prior displayed value and surface the error next to the affected scope.
- Avoid rebuilding the full TreeView for small state changes where property notifications can update the existing containers.
- Preserve Ctrl/Shift multi-selection, Ctrl+A within the current DWG, and Escape behavior; document the shortcuts in a small help affordance.

Primary files:

- `CAD Manager/CADManagerWindow.xaml`
- `CAD Manager/CADManagerWindow.xaml.cs`
- `CAD Manager/ViewModels/TreeViewControls.cs`
- `CAD Manager/Models/DWGModels.cs`

Acceptance:

- Press feedback appears immediately, before the Revit operation completes.
- Pending and failed states are distinguishable without relying solely on color.
- Selection survives filtering and refreshes predictably.
- Checkbox behavior passes single-row, parent-row, multi-row, filtered, and mixed-state test cases.

### Phase 5 — Secondary tool redesign

Principles: wayfinding, agency, simplicity.

Apply to Views:

- Keep the DataGrid with extended multi-row selection.
- Add a selected-count summary and a clear primary action label, such as “Apply to 4 Views.”
- Preserve search text and selection while the window remains open.
- Keep Cancel/Close non-destructive and keyboard accessible.
- Consider view-type filtering only after the modeless foundation is stable.

Line Graphics:

- Present Pattern, Color, and Weight as a compact inspector.
- Clearly represent mixed values and “No Override” as distinct states.
- Add a live summary of the target scope.
- Keep Apply explicit; do not modify Revit continuously while a ComboBox is being explored.
- Separate “Clear Overrides” spatially and visually from Apply/Close, while relying on Revit Undo for forgiveness.

Notifications:

- Add an inline/modeless status host for status, completion, warning, and error feedback.
- Routine completion disappears automatically after a short interval but remains available to assistive technology.
- Errors persist until dismissed and include a concrete recovery action where possible.
- Remove duplicate event subscriptions from the current universal popup implementation during replacement.

Acceptance:

- Every secondary tool names its scope, state, and next action.
- Apply/Clear operations can be completed without blocking Revit.
- Routine operations no longer produce modal success dialogs.

### Phase 6 — Accessibility and adaptive behavior

Principles: flexibility, responsibility.

Work:

- Add `AutomationProperties.Name`, `HelpText`, and meaningful control names to every icon-only action.
- Restore visible keyboard focus; remove `FocusVisualStyle="{x:Null}"` overrides.
- Define logical tab order and focus restoration when modeless tools close.
- Set default and cancel commands where applicable without relying on modal `DialogResult`.
- Add access keys to important labeled commands.
- Respect `SystemParameters.HighContrast` with near-solid surfaces and explicit borders.
- Respect `SystemParameters.ClientAreaAnimation`; disable nonessential motion when system animation is disabled.
- Test Narrator announcements for visibility, halftone, selection count, pending status, completion, and errors.
- Verify layout at 100%, 150%, and 200% scaling and with longer localized labels.

Acceptance:

- Every workflow is operable without a mouse.
- Every icon-only control has an accessible name.
- Focus is always visible and returns to the invoking control.
- High-contrast mode retains hierarchy and control boundaries.
- No important state is communicated only through color.

### Phase 7 — Restrained, interruptible motion

Principles: response, spatial consistency, craft.

Motion is deliberately last. This plugin does not need a general spring engine.

Work:

- Add 80–100 ms press feedback to command and row-action controls.
- Use 150–200 ms opacity/color transitions for selection, status changes, and theme transitions.
- If a modeless secondary window animates, use a restrained opacity/scale entrance anchored to the invoking control and a symmetric exit.
- Do not animate layout dimensions in the main TreeView.
- Begin new WPF animations from the current presentation value and stop/retarget existing animations rather than queueing them.
- Avoid bounce unless a future direct-manipulation gesture genuinely carries momentum.
- Skip motion entirely when system animation is disabled; keep state feedback instantaneous.

Acceptance:

- Motion never delays input or completion.
- Repeated or reversed actions do not jump or queue stale animations.
- No animation is required to understand state.
- Scrolling and TreeView interaction remain smooth on large projects.

### Phase 8 — Reduce UI coupling

Principles: craft and longevity.

Work:

- Extract selection/range logic from `CADManagerWindow` into a presentation-focused `SelectionController`.
- Move secondary-window lifecycle into `WindowCoordinator`.
- Move transient operation state into an `OperationStatus` model/service.
- Keep search filtering in the existing helper initially; expose it through bindable state rather than direct control mutation in a later change.
- Remove dead title-bar handlers and stale legacy/generated window references.
- Prefer shared commands and small controllers over a large one-shot MVVM rewrite.

Acceptance:

- `CADManagerWindow` no longer creates modal windows or formats notification content.
- Window, selection, and operation lifecycle code can be tested independently from Revit model writes.
- External-event handlers retain one responsibility each.
- The main window code-behind is materially smaller and contains primarily view wiring.

## Recommended PR breakdown

1. Baseline, ignore rules, and smoke-test checklist.
2. Modeless request/result infrastructure and apply-to-views conversion.
3. Modeless line-graphics inspector and notification host.
4. Design tokens, shared styles, and light/dark contrast work.
5. Native window chrome, command-bar hierarchy, and status row.
6. Tree hit targets, pending feedback, and state recovery.
7. Keyboard, automation, high-contrast, and DPI accessibility.
8. Restrained motion and reduced-motion behavior.
9. Main-window controller extraction and dead-code cleanup.

Each PR should be independently buildable and should leave the plugin usable.

## Validation matrix

Run each affected workflow in:

- Revit 2024 / .NET Framework 4.8.
- Revit 2026 / .NET 8 build path when available.
- Light, dark, and Windows high-contrast modes.
- 100%, 150%, and 200% display scaling.
- Keyboard-only navigation and Windows Narrator.
- Small and large DWG/layer sets.

Required scenarios:

- Rapidly toggle visibility and halftone without corrupting queued handler state.
- Open a modeless inspector, switch active view/document, then Apply.
- Delete or invalidate a target while a modeless tool remains open.
- Filter, multi-select, clear the filter, and verify selection consistency.
- Apply to multiple views and confirm one coherent Revit undo operation.
- Close/reopen each modeless tool without leaked handlers or stale owners.
- Change theme while secondary windows are open.
- Minimize, restore, resize, move between monitors, and restart Revit.

## Definition of done

- Zero plugin-owned `ShowDialog()` calls.
- Zero direct Revit document writes from window or control code.
- Zero intentionally suppressed focus visuals without an accessible replacement.
- All icon-only controls have accessible names and at least 32-DIP pointer targets.
- Routine success feedback is non-blocking.
- Pending external events cannot have their request data overwritten.
- Light, dark, and high-contrast themes meet contrast requirements.
- Motion respects system settings and never gates interaction.
- Revit remains interactive whenever plugin UI is visible.

## Explicit non-goals

- Copying macOS window controls or visual conventions onto Windows.
- Adding glass, blur, bounce, or animation merely for decoration.
- Replacing WPF with WinUI, MAUI, Blazor, or another UI framework.
- Introducing Revit 2025+ APIs into shared code paths.
- Rewriting the entire plugin to MVVM before user-facing improvements ship.
