using Autodesk.Revit.DB;
using System;
using CAD_Manager.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

namespace CAD_Manager.ViewModels
{
    public class DWGVisibilityController
    {
        private readonly Document _document;
        private readonly View _view;

        public DWGVisibilityController(Document document, View view)
        {
            _document = document;
            _view = view;
        }

        /// <summary>
        /// Collects all DWGs in the current view, ensuring Layer "0" is explicitly included.
        /// </summary>
        public List<DWGNode> CollectDWGs()
        {
            List<DWGNode> dwgNodes = new List<DWGNode>();

            FilteredElementCollector collector = new FilteredElementCollector(_document, _view.Id)
                .OfClass(typeof(ImportInstance));

            foreach (Element element in collector)
            {
                if (element is ImportInstance importInstance)
                {
                    ElementType type = _document.GetElement(importInstance.GetTypeId()) as ElementType;
                    string dwgName = type?.Name?.Normalize(NormalizationForm.FormKC) ?? "Unknown DWG";

                    DWGNode dwgNode = new DWGNode
                    {
                        Name = dwgName,
                        IsChecked = !_view.GetCategoryHidden(importInstance.Category.Id),
                        Layers = CollectLayers(importInstance)
                    };

                    if (importInstance.Category != null &&
                        importInstance.Category.Name?.Normalize(NormalizationForm.FormKC) == "0" &&
                        !dwgNode.Layers.Any(layer => layer.Name.Equals("0", StringComparison.CurrentCultureIgnoreCase)))
                    {
                        bool isLayerZeroVisible = !_view.GetCategoryHidden(importInstance.Category.Id);
                        var layerZeroParams = GetCategoryOverridesParams(_view, importInstance.Category.Id);
                        
                        dwgNode.Layers.Add(new LayerNode
                        {
                            Name = "0",
                            IsChecked = isLayerZeroVisible,
                            LinePattern = layerZeroParams.pattern,
                            LineColor = layerZeroParams.color,
                            LineWeight = layerZeroParams.weight
                        });
                    }

                    dwgNode.Layers = dwgNode.Layers.OrderBy(layer => layer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    
                    // Capture DWG level overrides
                    var dwgParams = GetCategoryOverridesParams(_view, importInstance.Category.Id);
                    dwgNode.LinePattern = dwgParams.pattern;
                    dwgNode.LineColor = dwgParams.color;
                    dwgNode.LineWeight = dwgParams.weight;
                    dwgNode.IsHalftone = dwgParams.halftone;

                    dwgNodes.Add(dwgNode);
                }
            }

            dwgNodes = dwgNodes.OrderBy(dwg => dwg.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return dwgNodes;
        }
        /// <summary>
        /// Collects layers for a given DWG, explicitly handling Layer "0".
        /// </summary>
        private List<LayerNode> CollectLayers(ImportInstance importInstance)
        {
            List<LayerNode> layers = new List<LayerNode>();

            if (importInstance.Category != null && !string.IsNullOrEmpty(importInstance.Category.Name))
            {
                bool isLayerZeroVisible = !_view.GetCategoryHidden(importInstance.Category.Id);
                var p = GetCategoryOverridesParams(_view, importInstance.Category.Id);

                layers.Add(new LayerNode
                {
                    Name = importInstance.Category.Name.Normalize(NormalizationForm.FormKC),
                    IsChecked = isLayerZeroVisible,
                    LinePattern = p.pattern,
                    LineColor = p.color,
                    LineWeight = p.weight
                });
            }

            foreach (Category subCategory in importInstance.Category.SubCategories)
            {
                if (subCategory != null && !string.IsNullOrEmpty(subCategory.Name))
                {
                    bool isLayerVisible = !_view.GetCategoryHidden(subCategory.Id);
                    var p = GetCategoryOverridesParams(_view, subCategory.Id);

                    layers.Add(new LayerNode
                    {
                        Name = subCategory.Name.Normalize(NormalizationForm.FormKC),
                        IsChecked = isLayerVisible,
                        LinePattern = p.pattern,
                        LineColor = p.color,
                        LineWeight = p.weight
                    });
                }
            }

            layers = layers.OrderBy(layer => layer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            return layers;
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

                // Pattern
                if (overrides.ProjectionLinePatternId != ElementId.InvalidElementId)
                {
                    var patElem = _document.GetElement(overrides.ProjectionLinePatternId) as LinePatternElement;
                    pat = patElem?.Name;
                }

                // Color
                if (overrides.ProjectionLineColor.IsValid && 
                   (overrides.ProjectionLineColor.Red != 0 || overrides.ProjectionLineColor.Green != 0 || overrides.ProjectionLineColor.Blue != 0)) // Check for non-black default if valid? Or just validity.
                {
                     // Note: InvalidColorValue is often (0,0,0) but IsValid is false.
                     // A valid black override is (0,0,0) and IsValid=true.
                     // Color helper:
                     col = $"#{overrides.ProjectionLineColor.Red:X2}{overrides.ProjectionLineColor.Green:X2}{overrides.ProjectionLineColor.Blue:X2}";
                }
                
                // Weight
                var w = overrides.ProjectionLineWeight;
                if (w > 0) wt = w;

                return (pat, col, wt, ht);
            }
            catch
            {
                return (null, null, null, false);
            }
        }
        /// <summary>
        /// Applies visibility settings to DWGs and their layers.
        /// </summary>
        public void ApplyVisibility(List<DWGNode> dwgNodes)
        {
            ElementId templateId = _view.ViewTemplateId;
            View templateView = templateId != ElementId.InvalidElementId ? _document.GetElement(templateId) as View : null;
            View viewToModify = templateView ?? _view;

            using (Transaction trans = new Transaction(_document, "Apply DWG Visibility"))
            {
                trans.Start();

                foreach (DWGNode dwgNode in dwgNodes)
                {
                    if (dwgNode.IsLinkedDWG)
                    {
                        ApplyLinkedDWGVisibility(viewToModify, dwgNode);
                    }
                    else
                    {
                        ImportInstance importInstance = FindImportInstanceByName(dwgNode.Name);
                        if (importInstance != null)
                        {
                            SetCategoryProperties(viewToModify, importInstance.Category, dwgNode.IsChecked, dwgNode);

                            foreach (LayerNode layer in dwgNode.Layers)
                            {
                                Category layerCategory = FindLayerCategory(importInstance, layer.Name);
                                if (layerCategory != null)
                                    SetCategoryProperties(viewToModify, layerCategory, layer.IsChecked, layer);
                            }
                        }
                    }
                }

                trans.Commit();
            }
        }

        /// <summary>
        /// Applies visibility and graphic overrides for a DWG embedded in a linked Revit model.
        /// </summary>
        private void ApplyLinkedDWGVisibility(View viewToModify, DWGNode dwgNode)
        {
            try
            {
                if (dwgNode.RevitLinkInstanceId == null || dwgNode.RevitLinkInstanceId == ElementId.InvalidElementId)
                    return;

                RevitLinkInstance linkInst = _document.GetElement(dwgNode.RevitLinkInstanceId) as RevitLinkInstance;
                if (linkInst == null) return;

                Document linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc == null) return;

                ImportInstance importInstance = FindImportInstanceInLinkedDoc(linkedDoc, dwgNode.Name);
                if (importInstance == null) return;

                RevitLinkGraphicsSettings settings = viewToModify.GetLinkOverrides(linkInst.Id);
                if (settings == null) settings = new RevitLinkGraphicsSettings();

                settings.LinkVisibilityType = RevitLinkGraphicsSettings.LinkVisibilityType.Custom;

                // DWG-level visibility and overrides
                settings.SetCategoryHidden(importInstance.Category.Id, !dwgNode.IsChecked);
                settings.SetCategoryOverrides(importInstance.Category.Id, BuildOverrideSettings(dwgNode));

                // Layer-level visibility and overrides
                foreach (LayerNode layer in dwgNode.Layers)
                {
                    Category layerCategory = FindLayerCategoryInLinkedDoc(importInstance, layer.Name);
                    if (layerCategory != null)
                    {
                        settings.SetCategoryHidden(layerCategory.Id, !layer.IsChecked);
                        settings.SetCategoryOverrides(layerCategory.Id, BuildOverrideSettings(layer));
                    }
                }

                viewToModify.SetLinkOverrides(linkInst.Id, settings);
            }
            catch { /* skip on failure */ }
        }

        /// <summary>
        /// Builds an OverrideGraphicSettings from a DWGNode or LayerNode's stored properties.
        /// </summary>
        private OverrideGraphicSettings BuildOverrideSettings(object node)
        {
            string patternName = null;
            string colorHex = null;
            int? weight = null;
            bool halftone = false;

            if (node is DWGNode dwg)
            {
                patternName = dwg.LinePattern;
                colorHex = dwg.LineColor;
                weight = dwg.LineWeight;
                halftone = dwg.IsHalftone;
            }
            else if (node is LayerNode layer)
            {
                patternName = layer.LinePattern;
                colorHex = layer.LineColor;
                weight = layer.LineWeight;
            }

            OverrideGraphicSettings settings = new OverrideGraphicSettings();

            if (!string.IsNullOrEmpty(colorHex))
            {
                var c = ParseColorHex(colorHex);
                if (c != null) settings.SetProjectionLineColor(c);
            }

            if (weight.HasValue && weight.Value > 0)
                settings.SetProjectionLineWeight(weight.Value);

            if (!string.IsNullOrEmpty(patternName))
            {
                ElementId patId = GetPatternId(patternName);
                if (patId != ElementId.InvalidElementId)
                    settings.SetProjectionLinePatternId(patId);
            }

            if (halftone)
                settings.SetHalftone(true);

            return settings;
        }

        /// <summary>
        /// Sets visibility and graphic overrides for a specific category.
        /// </summary>
        private void SetCategoryProperties(View view, Category category, bool isVisible, object node)
        {
            try
            {
                if (category == null) return;

                view.SetCategoryHidden(category.Id, !isVisible);
                view.SetCategoryOverrides(category.Id, BuildOverrideSettings(node));
            }
            catch
            {
                // Handle unmodifiable categories
            }
        }

        private Autodesk.Revit.DB.Color ParseColorHex(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return null;
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new Autodesk.Revit.DB.Color(r, g, b);
                }
            }
            catch {}
            return null;
        }

        private ElementId GetPatternId(string name)
        {
             return new FilteredElementCollector(_document)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))?.Id 
                ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Finds the ImportInstance by name.
        /// </summary>
        private ImportInstance FindImportInstanceByName(string name)
        {
            var key = name?.Normalize(NormalizationForm.FormKC);

            FilteredElementCollector collector = new FilteredElementCollector(_document)
                .OfClass(typeof(ImportInstance));

            foreach (Element element in collector)
            {
                if (element is ImportInstance importInstance &&
                    _document.GetElement(importInstance.GetTypeId()) is ElementType type &&
                    (type.Name?.Normalize(NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase))
                {
                    return importInstance;
                }
            }

            return null;
        }
        /// <summary>
        /// Finds a layer category by name.
        /// </summary>
        private Category FindLayerCategory(ImportInstance importInstance, string layerName)
        {
            var key = layerName?.Normalize(NormalizationForm.FormKC);
            foreach (Category subCategory in importInstance.Category.SubCategories)
            {
                if ((subCategory.Name?.Normalize(NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase))
                {
                    return subCategory;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds an ImportInstance by DWG name within a linked document.
        /// </summary>
        private ImportInstance FindImportInstanceInLinkedDoc(Document linkedDoc, string name)
        {
            var key = name?.Normalize(NormalizationForm.FormKC);

            foreach (Element element in new FilteredElementCollector(linkedDoc).OfClass(typeof(ImportInstance)))
            {
                if (element is ImportInstance importInstance &&
                    linkedDoc.GetElement(importInstance.GetTypeId()) is ElementType type &&
                    (type.Name?.Normalize(NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase))
                {
                    return importInstance;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a layer category by name within a linked ImportInstance.
        /// </summary>
        private Category FindLayerCategoryInLinkedDoc(ImportInstance importInstance, string layerName)
        {
            var key = layerName?.Normalize(NormalizationForm.FormKC);

            if (importInstance.Category?.Name?.Normalize(NormalizationForm.FormKC)
                    .Equals(key, StringComparison.CurrentCultureIgnoreCase) == true)
                return importInstance.Category;

            foreach (Category subCategory in importInstance.Category.SubCategories)
            {
                if ((subCategory.Name?.Normalize(NormalizationForm.FormKC))
                        .Equals(key, StringComparison.CurrentCultureIgnoreCase))
                    return subCategory;
            }

            return null;
        }
    }
}
