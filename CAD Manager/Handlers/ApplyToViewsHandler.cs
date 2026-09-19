using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Handlers
{
    public class ApplyToViewsHandler : IExternalEventHandler
    {
        private readonly object _requestLock = new object();
        private ApplyToViewsRequest _pendingRequest;

        public bool TrySubmit(
            Document document,
            ElementId sourceViewId,
            IEnumerable<ElementId> targetViewIds,
            Action<string> onComplete,
            Action<string> onError)
        {
            if (document == null ||
                sourceViewId == null ||
                sourceViewId == ElementId.InvalidElementId ||
                targetViewIds == null)
            {
                return false;
            }

            List<ElementId> targets = new HashSet<ElementId>(targetViewIds)
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .ToList();

            if (targets.Count == 0)
                return false;

            lock (_requestLock)
            {
                if (_pendingRequest != null)
                    return false;

                _pendingRequest = new ApplyToViewsRequest(
                    document,
                    sourceViewId,
                    targets,
                    onComplete,
                    onError);
                return true;
            }
        }

        public void CancelPendingRequest()
        {
            lock (_requestLock)
            {
                _pendingRequest = null;
            }
        }

        public void Execute(UIApplication app)
        {
            ApplyToViewsRequest request;
            lock (_requestLock)
            {
                request = _pendingRequest;
            }

            if (request == null)
                return;

            UIDocument uiDocument = app?.ActiveUIDocument;
            Document document = uiDocument?.Document;

            if (document == null || request.Document == null || !document.Equals(request.Document))
            {
                CompleteRequest(request, false, "The source document is no longer active. Reopen Apply to Views and try again.");
                return;
            }

            try
            {
                View sourceView = document.GetElement(request.SourceViewId) as View;
                if (sourceView == null)
                {
                    CompleteRequest(request, false, "The source view is no longer available.");
                    return;
                }

                List<View> targetViews = request.TargetViewIds
                    .Select(id => document.GetElement(id) as View)
                    .Where(view => view != null && !view.IsTemplate)
                    .ToList();

                if (targetViews.Count != request.TargetViewIds.Count)
                {
                    CompleteRequest(request, false, "One or more target views are no longer available. Refresh the list and try again.");
                    return;
                }

                string report;
                using (Transaction trans = new Transaction(document, "Apply Settings to Views"))
                {
                    trans.Start();

                    // Track results for each view
                    Dictionary<string, HashSet<string>> viewResults = new Dictionary<string, HashSet<string>>();
                    HashSet<string> allDWGsInSource = new HashSet<string>();

                    // First, collect all DWG names from source view
                    FilteredElementCollector sourceCollector = new FilteredElementCollector(document, sourceView.Id)
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
                    foreach (View targetView in targetViews)
                    {
                        HashSet<string> appliedDWGs = CopyCategoryOverrides(document, sourceView, targetView);
                        viewResults[targetView.Name] = appliedDWGs;
                    }

                    trans.Commit();

                    // Generate detailed report
                    report = GenerateReport(viewResults, allDWGsInSource);
                }

                CompleteRequest(request, true, report);
            }
            catch (Exception ex)
            {
                CompleteRequest(request, false, $"Error applying settings: {ex.Message}");
            }
        }

        private void CompleteRequest(ApplyToViewsRequest request, bool succeeded, string message)
        {
            lock (_requestLock)
            {
                if (!ReferenceEquals(_pendingRequest, request))
                    return;

                _pendingRequest = null;
            }

            if (succeeded)
                request.OnComplete?.Invoke(message);
            else
                request.OnError?.Invoke(message);
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

        private HashSet<string> CopyCategoryOverrides(Document document, View sourceView, View targetView)
        {
            // Get all ImportInstance categories from the source view
            FilteredElementCollector sourceCollector = new FilteredElementCollector(document, sourceView.Id)
                .OfClass(typeof(ImportInstance));

            // Get all ImportInstance categories from the target view
            FilteredElementCollector targetCollector = new FilteredElementCollector(document, targetView.Id)
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
                            CopyCategorySettings(document, sourceView, targetView, dwgCategory);
                            appliedDWGNames.Add(dwgCategory.Name);
                        }

                        // Copy layer (subcategory) settings
                        foreach (Category subCategory in dwgCategory.SubCategories)
                        {
                            if (subCategory != null && processedCategories.Add(subCategory.Id))
                            {
                                CopyCategorySettings(document, sourceView, targetView, subCategory);
                            }
                        }
                    }
                }
            }

            return appliedDWGNames;
        }

        private void CopyCategorySettings(Document document, View sourceView, View targetView, Category category)
        {
            try
            {
                // Check if target view has a template - if so, apply to template instead
                ElementId templateId = targetView.ViewTemplateId;
                View viewToModify = targetView;
                
                if (templateId != ElementId.InvalidElementId)
                {
                    View templateView = document.GetElement(templateId) as View;
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

        private sealed class ApplyToViewsRequest
        {
            public ApplyToViewsRequest(
                Document document,
                ElementId sourceViewId,
                List<ElementId> targetViewIds,
                Action<string> onComplete,
                Action<string> onError)
            {
                Document = document;
                SourceViewId = sourceViewId;
                TargetViewIds = targetViewIds;
                OnComplete = onComplete;
                OnError = onError;
            }

            public Document Document { get; }
            public ElementId SourceViewId { get; }
            public List<ElementId> TargetViewIds { get; }
            public Action<string> OnComplete { get; }
            public Action<string> OnError { get; }
        }
    }
}
