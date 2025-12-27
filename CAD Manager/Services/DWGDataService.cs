using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CAD_Manager.Services
{
    public class DWGDataService
    {
        public List<DWGNode> CollectDWGNodes(Document doc, View currentView)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc, currentView.Id)
                .OfClass(typeof(ImportInstance));

            List<DWGNode> dwgNodes = new List<DWGNode>();

            foreach (Element element in collector)
            {
                if (element is ImportInstance importInstance)
                {
                    string dwgName = GetDWGName(doc, importInstance);
                    bool isCategoryHidden = currentView.GetCategoryHidden(importInstance.Category.Id);
                    bool isDWGVisible = !isCategoryHidden;
                    
                    // Get Category Overrides (includes Halftone)
                    var dwgParams = GetCategoryOverridesParams(currentView, importInstance.Category.Id);
                    bool isHalftone = dwgParams.halftone;
                    
                    List<LayerNode> layers = GetDWGLayers(importInstance, currentView);

                    DWGNode dwgNode = new DWGNode
                    {
                        Name = dwgName,
                        IsChecked = isDWGVisible,
                        IsHalftone = isHalftone,
                        Layers = layers,
                        ElementId = importInstance.Id,
                        LinePattern = dwgParams.pattern,
                        LineColor = dwgParams.color,
                        LineWeight = dwgParams.weight
                    };

                    dwgNodes.Add(dwgNode);
                }
            }

            return dwgNodes.OrderBy(dwg => dwg.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private string GetDWGName(Document doc, ImportInstance importInstance)
        {
            ElementType type = doc.GetElement(importInstance.GetTypeId()) as ElementType;
            return type?.Name?.Normalize(NormalizationForm.FormKC) ?? "Unknown DWG Name";
        }

        private List<LayerNode> GetDWGLayers(ImportInstance importInstance, View currentView)
        {
            List<LayerNode> layers = new List<LayerNode>();
            HashSet<string> uniqueLayers = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            if (importInstance.Category != null)
            {
                string rootLayerName = importInstance.Category.Name?.Normalize(NormalizationForm.FormKC);

                if (rootLayerName == "0" && uniqueLayers.Add("0"))
                {
                    bool isLayerZeroVisible = !currentView.GetCategoryHidden(importInstance.Category.Id);
                    var p = GetCategoryOverridesParams(currentView, importInstance.Category.Id);
                    
                    layers.Add(new LayerNode
                    {
                        Name = "0",
                        IsChecked = isLayerZeroVisible,
                        ElementId = importInstance.Category.Id,
                        LinePattern = p.pattern,
                        LineColor = p.color,
                        LineWeight = p.weight
                    });
                }
            }

            foreach (Category subCategory in importInstance.Category.SubCategories)
            {
                if (subCategory != null && !string.IsNullOrEmpty(subCategory.Name))
                {
                    string subName = subCategory.Name.Normalize(NormalizationForm.FormKC);
                    if (uniqueLayers.Add(subName))
                    {
                        bool isLayerVisible = !currentView.GetCategoryHidden(subCategory.Id);
                        var p = GetCategoryOverridesParams(currentView, subCategory.Id);

                        layers.Add(new LayerNode
                        {
                            Name = subName,
                            IsChecked = isLayerVisible,
                            ElementId = subCategory.Id,
                            LinePattern = p.pattern,
                            LineColor = p.color,
                            LineWeight = p.weight
                        });
                    }
                }
            }

            return layers.OrderBy(layer => layer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private (string pattern, string color, int? weight, bool halftone) GetCategoryOverridesParams(View view, ElementId catId)
        {
            try
            {
                OverrideGraphicSettings overrides = view.GetCategoryOverrides(catId);
                
                string pat = null;
                string col = null;
                int? wt = null;
                bool ht = overrides.Halftone;

                if (overrides.ProjectionLinePatternId != ElementId.InvalidElementId)
                {
                    var patElem = view.Document.GetElement(overrides.ProjectionLinePatternId) as LinePatternElement;
                    pat = patElem?.Name;
                }

                if (overrides.ProjectionLineColor.IsValid)
                {
                     col = $"#{overrides.ProjectionLineColor.Red:X2}{overrides.ProjectionLineColor.Green:X2}{overrides.ProjectionLineColor.Blue:X2}";
                }
                
                var w = overrides.ProjectionLineWeight;
                if (w > 0) wt = w;

                return (pat, col, wt, ht);
            }
            catch
            {
                return (null, null, null, false);
            }
        }
    }
}
