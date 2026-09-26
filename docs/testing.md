# Automated testing

CAD Manager has two automated layers that run without starting Revit, plus the existing live-Revit smoke test.

## Core tests

`Tests/CAD Manager.Core.Tests` contains xUnit tests for Revit-free state and rules. The project runs the same tests on .NET 8 and .NET Framework 4.8. The core project also targets .NET Standard 2.0; its C# 7.3 sources are linked into the add-in so WPF's temporary markup-compilation project does not lose the dependency.

From the repository root:

```powershell
dotnet test "Tests/CAD Manager.Core.Tests/CAD Manager.Core.Tests.csproj" -c Release
```

The current data helper and tests cover ordering, duplicate IDs, case-insensitive search, selection persistence through filtering, visible-selection counts, unknown IDs, and clearing selection.

Add a scenario by extending `TestData.cs`, then add one focused `[Fact]` to the matching test class. Keep Revit types out of the core project.

## WPF UI harness

`Tests/UiHarness` is an STA WPF executable that opens the production `ApplyToViewsWindow`, `LineGraphicsWindow`, and `UniversalPopupWindow`. It uses canned view data, a fake callback-based host, and `AutoDialogs` instead of Revit or native modal pickers.

Run it from the repository root:

```powershell
dotnet run --project "Tests/UiHarness/UiHarness.csproj" -c Release
```

The process exits with the number of failed checks. A successful run exits with `0` and writes:

- `Tests/UiHarness/out/ui-harness.log`
- `Tests/UiHarness/out/gallery/*.png`

Set `CAD_MANAGER_UI_TEST_OUT` to redirect all harness output. The harness removes its temporary theme-settings file at startup, so runs do not read or modify the user's CAD Manager preferences.

The current script checks:

- Apply to Views startup, empty selection, multi-selection, filtering, hidden selection persistence, empty results, fake-host request payload, completion status, long names, and light/dark rendering.
- Line Graphics mixed/no-override display values, staged edits, automatic color selection, Apply, Clear Overrides, persistent errors, and light/dark rendering.
- Modeless notification layout and light/dark rendering.
- Layer Selection Filters editing, Import hidden proposals from a fake active view, repeated imports without duplicates, and Save.
- Screenshot creation itself.

To add a UI scenario, extend `Program.cs` with a method that opens a production window, attaches `FakeHost`, uses `Find`, `Click`, and `Pump`, records assertions through `Check`, captures the important state, and always closes/disposes the window.

`AutoDialogs` implements the production `IDialogService`. Add future picker/confirmation methods to that interface and provide both a system implementation and an automatic implementation so the harness never blocks.

## Visual review

The screenshot gallery is an assertion aid, not a pixel-perfect golden-image test. Review new captures for clipping, overlap, unreadable contrast, incorrect theme resources, and controls that no longer fit at the tested size. The first harness run found and fixed stale status colors after live theme switching and an unreadable disabled primary-button state.

## What remains manual

The harness deliberately does not execute Revit API code. Continue to use `docs/manual-revit-ui-smoke-test.md` for:

- Ribbon startup and AppLoader reload behavior.
- `ExternalEvent` scheduling and cancellation.
- Document/view invalidation between request and execution.
- Transactions, category visibility, graphic overrides, view templates, and Revit Undo.
- Main-window workflows that still coordinate `UIDocument`, `Document`, `View`, and `ElementId` directly.
- Narrator, Windows High Contrast, multi-monitor DPI, and native file/color dialogs.

The next testability increment should move one main-window workflow at a time behind a plain-data host interface. Avoid a one-shot rewrite of the main Revit coordinator.
