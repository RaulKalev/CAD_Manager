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

            using (Transaction trans = new Transaction(_document, "Apply DWG Visibility"))
            {
                trans.Start();

                foreach (DWGNode dwgNode in dwgNodes)
                {
                    ImportInstance importInstance = FindImportInstanceByName(dwgNode.Name);
                    if (importInstance != null)
                    {
                        // Set DWG visibility and overrides
                        if (templateView != null)
                        {
                            SetCategoryProperties(templateView, importInstance.Category, dwgNode.IsChecked, dwgNode);
                        }
                        else
                        {
                            SetCategoryProperties(_view, importInstance.Category, dwgNode.IsChecked, dwgNode);
                        }

                        // Set layer visibility and overrides
                        foreach (LayerNode layer in dwgNode.Layers)
                        {
                            Category layerCategory = FindLayerCategory(importInstance, layer.Name);
                            if (layerCategory != null)
                            {
                                if (templateView != null)
                                {
                                    SetCategoryProperties(templateView, layerCategory, layer.IsChecked, layer);
                                }
                                else
                                {
                                    SetCategoryProperties(_view, layerCategory, layer.IsChecked, layer);
                                }
                            }
                        }
                    }
                }

                trans.Commit();
            }
        }

        /// <summary>
        /// Sets visibility and graphic overrides for a specific category.
        /// </summary>
        private void SetCategoryProperties(View view, Category category, bool isVisible, object node)
        {
            try
            {
                if (category == null) return;

                // 1. Visibility
                view.SetCategoryHidden(category.Id, !isVisible);

                // 2. Graphic Overrides - Create NEW settings to ensure "No Value = No Override"
                string patternName = null;
                string colorHex = null;
                int? weight = null;

                if (node is DWGNode dwg)
                {
                    patternName = dwg.LinePattern;
                    colorHex = dwg.LineColor;
                    weight = dwg.LineWeight;
                }
                else if (node is LayerNode layer)
                {
                    patternName = layer.LinePattern;
                    colorHex = layer.LineColor;
                    weight = layer.LineWeight;
                }

                // Create NEW settings to ensure "No Value = No Override" is enforced strictly
                OverrideGraphicSettings newSettings = new OverrideGraphicSettings();
                
                // Color
                if (!string.IsNullOrEmpty(colorHex))
                {
                    var c = ParseColorHex(colorHex);
                    if (c != null) newSettings.SetProjectionLineColor(c);
                }

                // Weight
                if (weight.HasValue && weight.Value > 0)
                {
                    newSettings.SetProjectionLineWeight(weight.Value);
                }

                // Pattern
                if (!string.IsNullOrEmpty(patternName))
                {
                    ElementId patId = GetPatternId(patternName);
                    if (patId != ElementId.InvalidElementId)
                    {
                        newSettings.SetProjectionLinePatternId(patId);
                    }
                }

                // Halftone (if available on node? DWGNode has it. LayerNode doesn't usually, but maybe inherited?)
                if (node is DWGNode dNode && dNode.IsHalftone)
                {
                    newSettings.SetHalftone(true);
                }

                view.SetCategoryOverrides(category.Id, newSettings);
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
    }
}
