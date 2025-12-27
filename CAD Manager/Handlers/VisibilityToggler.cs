using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using CAD_Manager.Models;
using CAD_Manager.ViewModels;
using System.Collections.Generic;

namespace CAD_Manager.Handlers
{
    public class VisibilityToggler : IExternalEventHandler
    {
        public List<DWGNode> DWGNodes { get; set; }
        public Document Document { get; set; }
        public View CurrentView { get; set; }

        public void Execute(UIApplication app)
        {
            if (DWGNodes == null || Document == null || CurrentView == null) return;

            DWGVisibilityController controller = new DWGVisibilityController(Document, CurrentView);
            controller.ApplyVisibility(DWGNodes);
        }

        public string GetName()
        {
            return "DWG Visibility Toggler";
        }
    }
}
