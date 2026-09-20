using CAD_Manager.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace CAD_Manager.UiHarness
{
    internal sealed class FakeHost
    {
        public List<string> Log { get; } = new List<string>();

        public void Attach(ApplyToViewsWindow window)
        {
            window.ApplyRequested += (sender, args) =>
            {
                Log.Add("APPLY_VIEWS " + string.Join(",", args.TargetViewIds));
                window.SetRequestState(true, "Applying settings in fake Revit…");
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => window.SetRequestState(
                        false,
                        $"Applied settings to {args.TargetViewIds.Count} views.",
                        false,
                        true)));
            };
        }

        public void Attach(LineGraphicsWindow window)
        {
            window.ApplyRequested += (sender, args) =>
            {
                string color = args.Color == null
                    ? "unchanged"
                    : $"{args.Color.Red},{args.Color.Green},{args.Color.Blue}";
                Log.Add($"APPLY_GRAPHICS clear={args.ClearOverrides} pattern={args.Pattern ?? "unchanged"} weight={args.Weight?.ToString() ?? "unchanged"} color={color}");
                window.SetRequestState(true, args.ClearOverrides
                    ? "Clearing overrides in fake Revit…"
                    : "Applying line graphics in fake Revit…");
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => window.MarkApplied(
                        args.ClearOverrides,
                        args.ClearOverrides
                            ? "Overrides cleared."
                            : "Line graphics applied.")));
            };
        }
    }
}
