using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using CAD_Manager.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CAD_Manager.ViewModels
{
    public class ColorOverrideController
    {
        private readonly Document _document;
        private readonly View _view;

        public ColorOverrideController(Document document, View view)
        {
            _document = document;
            _view = view;
        }

        /// <summary>
        /// Applies color overrides to DWGs and Layers.
        /// </summary>
        public void ApplyColorOverrides(List<DWGNode> dwgNodes, Autodesk.Revit.DB.Color color)
        {
            ElementId templateId = _view.ViewTemplateId;
            View templateView = templateId != ElementId.InvalidElementId ? _document.GetElement(templateId) as View : null;

            using (Transaction trans = new Transaction(_document, "Apply DWG and Layer Colors"))
            {
                trans.Start();

                foreach (DWGNode dwgNode in dwgNodes)
                {
                    ImportInstance importInstance = FindImportInstanceByName(dwgNode.Name);

                    if (importInstance != null)
                    {
                        OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
                        overrideSettings.SetProjectionLineColor(color);

                        View viewToModify = templateView ?? _view;

                        // Apply DWG Color Override
                        if (importInstance.Category != null)
                        {
                            viewToModify.SetCategoryOverrides(importInstance.Category.Id, overrideSettings);
                        }

                        // Apply Layer Overrides
                        foreach (LayerNode layer in dwgNode.Layers)
                        {
                            Category layerCategory = FindLayerCategory(importInstance, layer.Name);
                            if (layerCategory != null)
                            {
                                viewToModify.SetCategoryOverrides(layerCategory.Id, overrideSettings);
                            }
                        }
                    }
                }

                trans.Commit();
            }
        }

        private ImportInstance FindImportInstanceByName(string name)
        {
            return new FilteredElementCollector(_document)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .FirstOrDefault(importInstance =>
                {
                    var type = _document.GetElement(importInstance.GetTypeId()) as ElementType;
                    return type?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false;
                });
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
    }
}
