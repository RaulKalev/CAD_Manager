using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Handlers
{
    public class ApplyToViewsHandler : IExternalEventHandler
    {
        public Document Document { get; set; }
        public View SourceView { get; set; }
        public List<View> TargetViews { get; set; }
        public Action<string> OnComplete { get; set; }
        public Action<string> OnError { get; set; }

        public void Execute(UIApplication app)
        {
            if (Document == null || SourceView == null || TargetViews == null || TargetViews.Count == 0)
            {
                OnError?.Invoke("Invalid parameters");
                return;
            }

            try
            {
                using (Transaction trans = new Transaction(Document, "Apply Settings to Views"))
                {
                    trans.Start();

                    // Track results for each view
                    Dictionary<string, HashSet<string>> viewResults = new Dictionary<string, HashSet<string>>();
                    HashSet<string> allDWGsInSource = new HashSet<string>();

                    // First, collect all DWG names from source view
                    FilteredElementCollector sourceCollector = new FilteredElementCollector(Document, SourceView.Id)
                        .OfClass(typeof(ImportInstance));

                    foreach (Element element in sourceCollector)
                    {
                        if (element is ImportInstance importInstance && importInstance.Category != null)
                        {
                            string dwgName = importInstance.Category.Name;
                            allDWGsInSource.Add(dwgName);
                        }
                    }

                    // Apply to each target view and track what was applied
                    foreach (View targetView in TargetViews)
                    {
                        HashSet<string> appliedDWGs = CopyCategoryOverrides(SourceView, targetView);
                        viewResults[targetView.Name] = appliedDWGs;
                    }

                    trans.Commit();

                    // Generate detailed report
                    string report = GenerateReport(viewResults, allDWGsInSource);
                    OnComplete?.Invoke(report);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Error applying settings: {ex.Message}");
            }
        }

        private string GenerateReport(Dictionary<string, HashSet<string>> viewResults, HashSet<string> allDWGsInSource)
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine($"Applied settings from {allDWGsInSource.Count} DWG(s) in current view:\n");

            foreach (var viewResult in viewResults)
            {
                string viewName = viewResult.Key;
                HashSet<string> appliedDWGs = viewResult.Value;

                report.AppendLine($"{viewName}:");
                
                if (appliedDWGs.Count > 0)
                {
                    report.AppendLine($"  ✓ Applied: {string.Join(", ", appliedDWGs)}");
                }
                else
                {
                    report.AppendLine($"  X No matching DWGs found in this view");
                }

                // Show which DWGs were NOT found
                var notFound = allDWGsInSource.Except(appliedDWGs).ToList();
                if (notFound.Count > 0 && appliedDWGs.Count > 0)
                {
                    report.AppendLine($"  X Not found: {string.Join(", ", notFound)}");
                }
                
                report.AppendLine();
            }

            return report.ToString().TrimEnd();
        }

        private HashSet<string> CopyCategoryOverrides(View sourceView, View targetView)
        {
            // Get all ImportInstance categories from the source view
            FilteredElementCollector sourceCollector = new FilteredElementCollector(Document, sourceView.Id)
                .OfClass(typeof(ImportInstance));

            // Get all ImportInstance categories from the target view
            FilteredElementCollector targetCollector = new FilteredElementCollector(Document, targetView.Id)
                .OfClass(typeof(ImportInstance));

            HashSet<ElementId> targetCategoryIds = new HashSet<ElementId>();
            foreach (Element element in targetCollector)
            {
                if (element is ImportInstance importInstance && importInstance.Category != null)
                {
                    targetCategoryIds.Add(importInstance.Category.Id);
                }
            }

            HashSet<ElementId> processedCategories = new HashSet<ElementId>();
            HashSet<string> appliedDWGNames = new HashSet<string>();

            foreach (Element element in sourceCollector)
            {
                if (element is ImportInstance importInstance && importInstance.Category != null)
                {
                    Category dwgCategory = importInstance.Category;

                    // Only apply if this category exists in the target view
                    if (targetCategoryIds.Contains(dwgCategory.Id))
                    {
                        // Copy DWG-level category settings
                        if (processedCategories.Add(dwgCategory.Id))
                        {
                            CopyCategorySettings(sourceView, targetView, dwgCategory);
                            appliedDWGNames.Add(dwgCategory.Name);
                        }

                        // Copy layer (subcategory) settings
                        foreach (Category subCategory in dwgCategory.SubCategories)
                        {
                            if (subCategory != null && processedCategories.Add(subCategory.Id))
                            {
                                CopyCategorySettings(sourceView, targetView, subCategory);
                            }
                        }
                    }
                }
            }

            return appliedDWGNames;
        }

        private void CopyCategorySettings(View sourceView, View targetView, Category category)
        {
            try
            {
                // Check if target view has a template - if so, apply to template instead
                ElementId templateId = targetView.ViewTemplateId;
                View viewToModify = targetView;
                
                if (templateId != ElementId.InvalidElementId)
                {
                    View templateView = Document.GetElement(templateId) as View;
                    if (templateView != null)
                    {
                        viewToModify = templateView;
                    }
                }

                // Get visibility setting from source
                bool isHidden = sourceView.GetCategoryHidden(category.Id);
                viewToModify.SetCategoryHidden(category.Id, isHidden);

                // Get and copy graphic overrides
                OverrideGraphicSettings sourceOverrides = sourceView.GetCategoryOverrides(category.Id);
                OverrideGraphicSettings newOverrides = new OverrideGraphicSettings();

                // Copy halftone
                newOverrides.SetHalftone(sourceOverrides.Halftone);

                // Copy color
                if (sourceOverrides.ProjectionLineColor.IsValid)
                {
                    newOverrides.SetProjectionLineColor(sourceOverrides.ProjectionLineColor);
                }

                // Copy pattern
                if (sourceOverrides.ProjectionLinePatternId != ElementId.InvalidElementId)
                {
                    newOverrides.SetProjectionLinePatternId(sourceOverrides.ProjectionLinePatternId);
                }

                // Copy weight
                if (sourceOverrides.ProjectionLineWeight > 0)
                {
                    newOverrides.SetProjectionLineWeight(sourceOverrides.ProjectionLineWeight);
                }

                // Apply to target view (or its template)
                viewToModify.SetCategoryOverrides(category.Id, newOverrides);
            }
            catch
            {
                // Skip categories that can't be copied
            }
        }

        public string GetName()
        {
            return "Apply Settings to Views Handler";
        }
    }
}
