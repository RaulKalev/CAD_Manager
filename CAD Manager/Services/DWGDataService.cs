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
            var dwgNodes = CollectHostDWGNodes(doc, currentView);
            dwgNodes.AddRange(CollectLinkedDWGNodes(doc, currentView));
            return dwgNodes.OrderBy(dwg => dwg.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private List<DWGNode> CollectHostDWGNodes(Document doc, View currentView)
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

            return dwgNodes;
        }

        private List<DWGNode> CollectLinkedDWGNodes(Document doc, View currentView)
        {
            List<DWGNode> dwgNodes = new List<DWGNode>();

            try
            {
                FilteredElementCollector linkCollector = new FilteredElementCollector(doc, currentView.Id)
                    .OfClass(typeof(RevitLinkInstance));

                foreach (Element linkElement in linkCollector)
                {
                    if (!(linkElement is RevitLinkInstance linkInstance)) continue;

                    Document linkedDoc = linkInstance.GetLinkDocument();
                    if (linkedDoc == null) continue;

                    RevitLinkGraphicsSettings linkSettings = currentView.GetLinkOverrides(linkInstance.Id);
                    if (linkSettings == null) continue;

                    string linkName = (doc.GetElement(linkInstance.GetTypeId()) as ElementType)?.Name ?? linkInstance.Name;

                    FilteredElementCollector importCollector = new FilteredElementCollector(linkedDoc)
                        .OfClass(typeof(ImportInstance));

                    HashSet<ElementId> seenCategories = new HashSet<ElementId>();

                    foreach (Element importElement in importCollector)
                    {
                        if (!(importElement is ImportInstance importInstance)) continue;
                        if (importInstance.Category == null) continue;
                        if (!seenCategories.Add(importInstance.Category.Id)) continue;

                        string dwgName = GetDWGNameFromLinkedDoc(linkedDoc, importInstance);

                        bool isCustom = linkSettings.LinkVisibilityType == RevitLinkGraphicsSettings.LinkVisibilityType.Custom;
                        bool isDWGVisible = !isCustom || !linkSettings.IsCategoryHidden(importInstance.Category.Id);

                        var dwgParams = isCustom
                            ? GetLinkCategoryOverridesParams(linkSettings, importInstance.Category.Id, linkedDoc)
                            : (pattern: (string)null, color: (string)null, weight: (int?)null, halftone: false);

                        List<LayerNode> layers = GetLinkedDWGLayers(importInstance, linkSettings, isCustom, linkedDoc);

                        DWGNode dwgNode = new DWGNode
                        {
                            Name = dwgName,
                            IsChecked = isDWGVisible,
                            IsHalftone = dwgParams.halftone,
                            Layers = layers,
                            ElementId = importInstance.Id,
                            LinePattern = dwgParams.pattern,
                            LineColor = dwgParams.color,
                            LineWeight = dwgParams.weight,
                            IsLinkedDWG = true,
                            RevitLinkInstanceId = linkInstance.Id,
                            LinkedModelName = linkName
                        };

                        dwgNodes.Add(dwgNode);
                    }
                }
            }
            catch { /* non-critical: skip linked DWG collection on failure */ }

            return dwgNodes;
        }

        private string GetDWGNameFromLinkedDoc(Document linkedDoc, ImportInstance importInstance)
        {
            try
            {
                ElementType type = linkedDoc.GetElement(importInstance.GetTypeId()) as ElementType;
                return type?.Name?.Normalize(NormalizationForm.FormKC) ?? "Unknown DWG Name";
            }
            catch { return "Unknown DWG Name"; }
        }

        private List<LayerNode> GetLinkedDWGLayers(ImportInstance importInstance, RevitLinkGraphicsSettings linkSettings, bool isCustom, Document linkedDoc)
        {
            List<LayerNode> layers = new List<LayerNode>();
            HashSet<string> uniqueLayers = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            if (importInstance.Category != null)
            {
                string rootLayerName = importInstance.Category.Name?.Normalize(NormalizationForm.FormKC);

                if (rootLayerName == "0" && uniqueLayers.Add("0"))
                {
                    bool isLayerZeroVisible = !isCustom || !linkSettings.IsCategoryHidden(importInstance.Category.Id);
                    var p = isCustom
                        ? GetLinkCategoryOverridesParams(linkSettings, importInstance.Category.Id, linkedDoc)
                        : (pattern: (string)null, color: (string)null, weight: (int?)null, halftone: false);

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
                if (subCategory == null || string.IsNullOrEmpty(subCategory.Name)) continue;

                string subName = subCategory.Name.Normalize(NormalizationForm.FormKC);
                if (!uniqueLayers.Add(subName)) continue;

                bool isLayerVisible = !isCustom || !linkSettings.IsCategoryHidden(subCategory.Id);
                var p = isCustom
                    ? GetLinkCategoryOverridesParams(linkSettings, subCategory.Id, linkedDoc)
                    : (pattern: (string)null, color: (string)null, weight: (int?)null, halftone: false);

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

            return layers.OrderBy(layer => layer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private (string pattern, string color, int? weight, bool halftone) GetLinkCategoryOverridesParams(RevitLinkGraphicsSettings linkSettings, ElementId catId, Document linkedDoc)
        {
            try
            {
                OverrideGraphicSettings overrides = linkSettings.GetCategoryOverrides(catId);
                if (overrides == null) return (null, null, null, false);

                string pat = null;
                string col = null;
                int? wt = null;
                bool ht = overrides.Halftone;

                if (overrides.ProjectionLinePatternId != ElementId.InvalidElementId)
                {
                    var patElem = linkedDoc.GetElement(overrides.ProjectionLinePatternId) as LinePatternElement;
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
