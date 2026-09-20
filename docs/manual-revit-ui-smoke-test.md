# Manual Revit UI smoke test

Run this checklist in Revit 2024 and Revit 2026 before merging UI workflow changes.

## Setup

- Open a project containing at least two linked or imported DWGs with multiple layers.
- Open two graphical views; apply a view template to at least one view if available.
- Test once in the light theme and once in the dark theme.

## Modeless behavior

- Open CAD Manager, Apply to Views, and Line Graphics; confirm Revit remains interactive.
- Switch views while each secondary window is open.
- Close and reopen each secondary window; confirm only one instance of each tool is active.
- Confirm plugin-owned windows never dim or block the Revit window.
- Close CAD Manager while a visibility, halftone, graphics-read, graphics-write, or Apply to Views request is pending; confirm shutdown remains stable and no stale callback reopens UI.

## Apply to Views

- Search for views, use Ctrl/Shift multi-selection, and apply to multiple targets.
- Click Apply repeatedly while an operation is pending; confirm only one request runs.
- Switch documents or invalidate a target before Revit consumes the request; confirm a persistent inline error.
- Confirm one coherent Revit undo item is created.

## Line Graphics

- Open the inspector for one DWG, multiple selected DWGs, one layer, and layers across multiple DWGs.
- Confirm the scope summary and mixed values are correct.
- Change pattern, color, and weight, then Apply; confirm the inspector stays open and reports completion.
- Use Clear Overrides and confirm Revit Undo restores the prior state.
- Switch the active view before Apply; confirm no write occurs and an inline error explains the recovery.
- Start an Apply and close the inspector; confirm Revit remains stable and the main status reports the result.

## Notifications and accessibility

- Save, load, browse, refresh, and test a non-matching template; confirm routine results use the main status host.
- Confirm routine statuses dismiss after about 15 seconds, while a view or selection context change restores the context summary immediately.
- Force an operation failure; confirm the one-line status shows an error icon and text, is announced assertively, remains until dismissed, and restores the context summary after dismissal.
- Navigate every affected window with Tab and Shift+Tab; confirm focus is visible for keyboard navigation but no blue focus boundary remains after a mouse click.
- Use Enter for Apply and Escape/Close for non-destructive dismissal.
- With Narrator enabled, confirm scope, selection counts, pending state, completion, and errors are announced.
- Disable Windows client-area animations; confirm progress and ComboBox feedback remain understandable without animated transitions.

## Tree state and request safety

- Select DWGs and layers, filter the list, clear the filter, and confirm the same logical items remain selected.
- Collapse a DWG, refresh, and confirm its expansion state and all selected rows are preserved.
- Start a visibility or halftone update, then immediately try Save, Load, Browse, and Refresh; confirm each asks you to wait and does not replace queued request data.
- Load a saved preset and switch views before Revit consumes it; confirm no write occurs and the persistent error explains that the active view changed.
- Load a saved preset normally; confirm visibility, halftone, pattern, color, and weight are applied as one Revit undo item.

## Display checks

- Repeat at 100%, 150%, and 200% Windows display scaling.
- Move the windows between monitors and restore from minimized state.
- Confirm labels do not clip and controls remain usable with longer text.
