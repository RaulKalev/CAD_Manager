using CAD_Manager.Core;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace CAD_Manager.UiHarness
{
    internal sealed class AutoDialogs : IDialogService
    {
        public bool AcceptColor { get; set; } = true;
        public byte Red { get; set; } = 24;
        public byte Green { get; set; } = 122;
        public byte Blue { get; set; } = 255;
        public List<string> Transcript { get; } = new List<string>();

        public void PickColor(
            byte initialRed,
            byte initialGreen,
            byte initialBlue,
            Action<ColorDialogResult> completed)
        {
            Transcript.Add($"PICK_COLOR initial={initialRed},{initialGreen},{initialBlue} accepted={AcceptColor}");
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => completed?.Invoke(new ColorDialogResult(
                    AcceptColor,
                    Red,
                    Green,
                    Blue))));
        }
    }
}
