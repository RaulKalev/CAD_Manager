using System;

namespace CAD_Manager.Core
{
    public sealed class ColorDialogResult
    {
        public ColorDialogResult(bool accepted, byte red, byte green, byte blue)
        {
            Accepted = accepted;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public bool Accepted { get; }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
    }

    public interface IDialogService
    {
        void PickColor(
            byte initialRed,
            byte initialGreen,
            byte initialBlue,
            Action<ColorDialogResult> completed);
    }
}
