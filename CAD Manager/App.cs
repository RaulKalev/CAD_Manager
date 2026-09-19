using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Newtonsoft.Json;
using System.Linq;

namespace CAD_Manager
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private const string RibbonTabName = "RK Tools";
        private const string RibbonPanelName = "Tools";
        private const string RibbonButtonName = "CADManagerButton";
        private RibbonPanel ribbonPanel;
        private RibbonItem ribbonButton;

        public Result OnStartup(UIControlledApplication application)
        {
            // Several RK Tools add-ins share this tab and panel. AppLoader can
            // also restart an add-in without restarting Revit, so both must be
            // selected when they already exist rather than created unconditionally.
            ribbonPanel = application.CreateOrSelectPanel(RibbonTabName, RibbonPanelName);

            ribbonButton = ribbonPanel.GetItems()
                .FirstOrDefault(item => item.Name == RibbonButtonName);
            if (ribbonButton != null)
            {
                return Result.Succeeded;
            }

            // Create PushButton with embedded resource
            ribbonButton = ribbonPanel.CreatePushButton<CADManagerCommand>(RibbonButtonName)
                .SetLargeImage("Assets/layers.tiff")
                .SetText("CAD\nManager")
                .SetToolTip("Manage visibility of CADs and their layers visible in view.")
                .SetContextualHelp("https://raulkalev.github.io/rktools/");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // The panel is shared with other RK Tools add-ins; remove only the
            // command owned by this application.
            if (ribbonPanel != null && ribbonButton != null)
            {
                ribbonPanel.Remove(ribbonButton);
            }

            ribbonButton = null;
            ribbonPanel = null;
            return Result.Succeeded;
        }

    }
}
