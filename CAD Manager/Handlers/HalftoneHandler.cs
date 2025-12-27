using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Models;
using System.Collections.Generic;

namespace CAD_Manager.Handlers
{
    public class HalftoneHandler : IExternalEventHandler
    {
        public List<DWGNode> DWGNodes { get; set; }
        public Document Document { get; set; }
        public View CurrentView { get; set; }

        public void Execute(UIApplication app)
        {
            if (DWGNodes == null || Document == null || CurrentView == null) return;

            // Check if view has a template
            ElementId templateId = CurrentView.ViewTemplateId;
            View targetView = templateId != ElementId.InvalidElementId 
                ? Document.GetElement(templateId) as View 
                : CurrentView;

            using (Transaction t = new Transaction(Document, "Toggle Halftone"))
            {
                t.Start();

                foreach (var node in DWGNodes)
                {
                    Element element = Document.GetElement(node.ElementId);
                    if (element is ImportInstance importInstance && importInstance.Category != null)
                    {
                        // Get existing overrides from the target view (template or current)
                        OverrideGraphicSettings existingOverrides = targetView.GetCategoryOverrides(importInstance.Category.Id);
                        
                        // Create new settings and copy existing properties
                        OverrideGraphicSettings newOverrides = new OverrideGraphicSettings();
                        
                        // Copy existing graphic overrides
                        if (existingOverrides.ProjectionLineColor.IsValid)
                            newOverrides.SetProjectionLineColor(existingOverrides.ProjectionLineColor);
                        
                        if (existingOverrides.ProjectionLinePatternId != ElementId.InvalidElementId)
                            newOverrides.SetProjectionLinePatternId(existingOverrides.ProjectionLinePatternId);
                        
                        if (existingOverrides.ProjectionLineWeight > 0)
                            newOverrides.SetProjectionLineWeight(existingOverrides.ProjectionLineWeight);
                        
                        // Set the halftone value
                        newOverrides.SetHalftone(node.IsHalftone);
                        
                        // Apply the new overrides to the target view
                        targetView.SetCategoryOverrides(importInstance.Category.Id, newOverrides);
                    }
                }

                t.Commit();
            }
        }

        public string GetName()
        {
            return "DWG Halftone Toggler";
        }
    }
}
