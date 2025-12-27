using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace CAD_Manager
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            // Create Ribbon Panel on the default Add-Ins tab
            ribbonPanel = application.CreateRibbonPanel("Tools");

            // Create PushButton with embedded resource
            ribbonPanel.CreatePushButton<CADManagerCommand>()
                .SetLargeImage("Assets/layers.tiff")
                .SetText("CAD\nManager")
                .SetToolTip("Manage visibility of CADs and their layers visible in view.")
                .SetContextualHelp("https://raulkalev.github.io/rktools/");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

    }
}