using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using CAD_Manager.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CAD_Manager.Handlers
{
    public class ColorOverrideHandler : IExternalEventHandler
    {
        public List<DWGNode> DWGNodes { get; set; }
        public Autodesk.Revit.DB.Color OverrideColor { get; set; }
        public string LinePattern { get; set; }
        public int? LineWeight { get; set; }
        public bool ClearOverrides { get; set; }
        public bool IsLayerOverride { get; set; } // Distinguish between DWG and Layer overrides

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            View activeView = doc.ActiveView;

            if (DWGNodes == null || DWGNodes.Count == 0)
            {
                TaskDialog.Show("Error", "DWG nodes is null or empty. Cannot apply overrides.");
                return;
            }

            ElementId templateId = activeView.ViewTemplateId;
            View viewToModify = templateId != ElementId.InvalidElementId
                ? doc.GetElement(templateId) as View
                : activeView;

            if (viewToModify == null)
            {
                TaskDialog.Show("Error", "Failed to find the view for applying overrides.");
                return;
            }

            using (Transaction trans = new Transaction(doc, "Apply DWG and Layer Colors"))
            {
                try
                {
                    trans.Start();

                    // Override settings are now handled inside the loop per category to allow merging


                    foreach (DWGNode dwgNode in DWGNodes)
                    {
                        ImportInstance importInstance = doc.GetElement(dwgNode.ElementId) as ImportInstance;

                        if (importInstance != null && importInstance.Category != null)
                        {
                            // Determine target categories based on mode
                            var targetCategories = new List<Category>();
                            if (!IsLayerOverride)
                            {
                                targetCategories.Add(importInstance.Category);
                            }
                            else
                            {
                                foreach (LayerNode layer in dwgNode.Layers)
                                {
                                    Category layerCategory = FindLayerCategory(importInstance, layer.Name);
                                    if (layerCategory != null)
                                    {
                                        targetCategories.Add(layerCategory);
                                    }
                                }
                            }

                            // Apply overrides to each target category
                            foreach (Category cat in targetCategories)
                            {
                                OverrideGraphicSettings overrideSettings;
                                
                                if (ClearOverrides)
                                {
                                    overrideSettings = new OverrideGraphicSettings(); // Reset to default
                                }
                                else
                                {
                                    // Get existing overrides to merge with
                                    overrideSettings = viewToModify.GetCategoryOverrides(cat.Id);

                                    // Apply Color (if changed)
                                    if (OverrideColor != null)
                                    {
                                        overrideSettings.SetProjectionLineColor(OverrideColor);
                                    }

                                    // Apply Line Pattern (if changed)
                                    if (LinePattern != null)
                                    {
                                        if (LinePattern == "<No Override>")
                                        {
                                            // Explicitly clear pattern override
                                            overrideSettings.SetProjectionLinePatternId(ElementId.InvalidElementId);
                                        }
                                        else if (!string.IsNullOrEmpty(LinePattern))
                                        {
                                            var linePatternId = GetLinePatternId(doc, LinePattern);
                                            if (linePatternId != null && linePatternId != ElementId.InvalidElementId)
                                            {
                                                overrideSettings.SetProjectionLinePatternId(linePatternId);
                                            }
                                        }
                                    }

                                    // Apply Line Weight (if changed)
                                    if (LineWeight.HasValue)
                                    {
                                        if (LineWeight.Value == -1)
                                        {
                                            // Explicitly clear weight override
                                            overrideSettings.SetProjectionLineWeight(OverrideGraphicSettings.InvalidPenNumber);
                                        }
                                        else if (LineWeight.Value > 0)
                                        {
                                            overrideSettings.SetProjectionLineWeight(LineWeight.Value);
                                        }
                                    }
                                }

                                // Apply the merged settings back to the view
                                viewToModify.SetCategoryOverrides(cat.Id, overrideSettings);
                            }
                        }
                    }

                    trans.Commit();
                    // Success - no dialog needed
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    TaskDialog.Show("Error", $"Failed to apply category color override: {ex.Message}");
                }
            }
        }
        private Category FindLayerCategory(ImportInstance importInstance, string layerName)
        {
            var key = layerName?.Normalize(NormalizationForm.FormKC);
            return importInstance.Category?.SubCategories?
                .Cast<Category>()
                .FirstOrDefault(subCat =>
                    (subCat.Name?.Normalize(NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase));
        }

        private ElementId GetLinePatternId(Document doc, string patternName)
        {
            try
            {
                // Get all line pattern elements
                var linePatterns = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>();

                // Find matching pattern
                var pattern = linePatterns.FirstOrDefault(lp => 
                    lp.Name.Equals(patternName, StringComparison.OrdinalIgnoreCase));

                return pattern?.Id ?? ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }
        public string GetName()
        {
            return "Color Override Handler";
        }
    }
}
