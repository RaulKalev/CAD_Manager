using CAD_Manager.Core;
using System;

namespace CAD_Manager.Services
{
    public sealed class SystemDialogService : IDialogService
    {
        public void PickColor(
            byte initialRed,
            byte initialGreen,
            byte initialBlue,
            Action<ColorDialogResult> completed)
        {
            using (System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(initialRed, initialGreen, initialBlue)
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    completed?.Invoke(new ColorDialogResult(false, initialRed, initialGreen, initialBlue));
                    return;
                }

                completed?.Invoke(new ColorDialogResult(
                    true,
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B));
            }
        }
    }
}
