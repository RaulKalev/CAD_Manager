using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Helpers;
using CAD_Manager.Models;
using CAD_Manager.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CAD_Manager.Handlers
{
    public class VisibilityToggler : IExternalEventHandler
    {
        private readonly object _requestLock = new object();
        private VisibilityRequest _pendingRequest;

        // Retained for preset save/load and refresh workflows. Direct row changes use immutable requests.
        public List<DWGNode> DWGNodes { get; set; }
        public Document Document { get; set; }
        public View CurrentView { get; set; }

        public bool TrySubmit(
            Document document,
            ElementId viewId,
            IEnumerable<VisibilityChangeTarget> targets,
            Action<string> onComplete,
            Action<string> onError)
        {
            List<VisibilityChangeTarget> targetList = (targets ?? Enumerable.Empty<VisibilityChangeTarget>())
                .Where(target => target != null && target.ImportInstanceId != null && target.ImportInstanceId != ElementId.InvalidElementId)
                .Select(target => new VisibilityChangeTarget(target.ImportInstanceId, target.LayerName, target.IsVisible))
                .ToList();

            if (document == null || viewId == null || viewId == ElementId.InvalidElementId || targetList.Count == 0)
                return false;

            lock (_requestLock)
            {
                if (_pendingRequest != null)
                    return false;

                _pendingRequest = new VisibilityRequest(document, viewId, targetList, onComplete, onError);
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
            VisibilityRequest request;
            lock (_requestLock)
            {
                request = _pendingRequest;
            }

            if (request != null)
            {
                ExecuteRequest(app, request);
                return;
            }

            // Compatibility path for loading a complete saved preset.
            if (DWGNodes == null || Document == null || CurrentView == null)
                return;

            DWGVisibilityController controller = new DWGVisibilityController(Document, CurrentView);
            controller.ApplyVisibility(DWGNodes);
        }

        private void ExecuteRequest(UIApplication app, VisibilityRequest request)
        {
            Document document = app?.ActiveUIDocument?.Document;
            View activeView = document?.ActiveView;
            if (document == null || request.Document == null || !document.Equals(request.Document))
            {
                Complete(request, false, "The document changed before the visibility update could run.");
                return;
            }

            if (activeView == null || activeView.Id.GetIdValue() != request.ViewId.GetIdValue())
            {
                Complete(request, false, "The active view changed. Refresh CAD Manager and try again.");
                return;
            }

            try
            {
                View targetView = activeView.ViewTemplateId != ElementId.InvalidElementId
                    ? document.GetElement(activeView.ViewTemplateId) as View ?? activeView
                    : activeView;

                int resolvedTargetCount;
                Dictionary<long, VisibilityCategoryChange> changes = ResolveChanges(document, request.Targets, out resolvedTargetCount);
                if (resolvedTargetCount != request.Targets.Count)
                {
                    Complete(request, false, "One or more DWG layers are no longer available. Refresh and try again.");
                    return;
                }

                using (Transaction transaction = new Transaction(document, "Update DWG Visibility"))
                {
                    transaction.Start();
                    foreach (VisibilityCategoryChange change in changes.Values)
                    {
                        targetView.SetCategoryHidden(change.Category.Id, !change.IsVisible);
                    }
                    transaction.Commit();
                }

                int count = changes.Count;
                Complete(request, true, $"Updated visibility for {count} {(count == 1 ? "item" : "items")}.");
            }
            catch (Exception ex)
            {
                Complete(request, false, $"Failed to update visibility: {ex.Message}");
            }
        }

        private static Dictionary<long, VisibilityCategoryChange> ResolveChanges(
            Document document,
            IEnumerable<VisibilityChangeTarget> targets,
            out int resolvedTargetCount)
        {
            Dictionary<long, VisibilityCategoryChange> changes = new Dictionary<long, VisibilityCategoryChange>();
            resolvedTargetCount = 0;
            foreach (VisibilityChangeTarget target in targets)
            {
                ImportInstance importInstance = document.GetElement(target.ImportInstanceId) as ImportInstance;
                Category category = string.IsNullOrEmpty(target.LayerName)
                    ? importInstance?.Category
                    : FindLayerCategory(importInstance, target.LayerName);

                if (category != null)
                {
                    changes[category.Id.GetIdValue()] = new VisibilityCategoryChange(category, target.IsVisible);
                    resolvedTargetCount++;
                }
            }
            return changes;
        }

        private static Category FindLayerCategory(ImportInstance importInstance, string layerName)
        {
            string key = layerName?.Normalize(NormalizationForm.FormKC);
            return importInstance?.Category?.SubCategories?
                .Cast<Category>()
                .FirstOrDefault(category => string.Equals(
                    category.Name?.Normalize(NormalizationForm.FormKC),
                    key,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        private void Complete(VisibilityRequest request, bool succeeded, string message)
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

        public string GetName()
        {
            return "DWG Visibility Toggler";
        }

        private sealed class VisibilityCategoryChange
        {
            public VisibilityCategoryChange(Category category, bool isVisible)
            {
                Category = category;
                IsVisible = isVisible;
            }

            public Category Category { get; }
            public bool IsVisible { get; }
        }

        private sealed class VisibilityRequest
        {
            public VisibilityRequest(
                Document document,
                ElementId viewId,
                List<VisibilityChangeTarget> targets,
                Action<string> onComplete,
                Action<string> onError)
            {
                Document = document;
                ViewId = viewId;
                Targets = targets;
                OnComplete = onComplete;
                OnError = onError;
            }

            public Document Document { get; }
            public ElementId ViewId { get; }
            public List<VisibilityChangeTarget> Targets { get; }
            public Action<string> OnComplete { get; }
            public Action<string> OnError { get; }
        }
    }
}
