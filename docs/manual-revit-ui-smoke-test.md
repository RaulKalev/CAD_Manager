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
- Confirm success statuses dismiss after about six seconds and errors remain until dismissed.
- Navigate every affected window with Tab and Shift+Tab; confirm focus is visible.
- Use Enter for Apply and Escape/Close for non-destructive dismissal.
- With Narrator enabled, confirm scope, selection counts, pending state, completion, and errors are announced.

## Display checks

- Repeat at 100%, 150%, and 200% Windows display scaling.
- Move the windows between monitors and restore from minimized state.
- Confirm labels do not clip and controls remain usable with longer text.
