using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Helpers;
using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Handlers
{
    public class HalftoneHandler : IExternalEventHandler
    {
        private readonly object _requestLock = new object();
        private HalftoneRequest _pendingRequest;

        public Document Document { get; set; }
        public View CurrentView { get; set; }

        public bool HasPendingRequest
        {
            get
            {
                lock (_requestLock)
                {
                    return _pendingRequest != null;
                }
            }
        }

        public bool TrySubmit(
            Document document,
            ElementId viewId,
            IEnumerable<HalftoneChangeTarget> targets,
            Action<string> onComplete,
            Action<string> onError)
        {
            List<HalftoneChangeTarget> targetList = (targets ?? Enumerable.Empty<HalftoneChangeTarget>())
                .Where(target => target != null && target.ImportInstanceId != null && target.ImportInstanceId != ElementId.InvalidElementId)
                .Select(target => new HalftoneChangeTarget(target.ImportInstanceId, target.IsHalftone))
                .ToList();

            if (document == null || viewId == null || viewId == ElementId.InvalidElementId || targetList.Count == 0)
                return false;

            lock (_requestLock)
            {
                if (_pendingRequest != null)
                    return false;

                _pendingRequest = new HalftoneRequest(document, viewId, targetList, onComplete, onError);
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
            HalftoneRequest request;
            lock (_requestLock)
            {
                request = _pendingRequest;
            }

            if (request == null)
                return;

            Document document = app?.ActiveUIDocument?.Document;
            View activeView = document?.ActiveView;
            if (document == null || request.Document == null || !document.Equals(request.Document))
            {
                Complete(request, false, "The document changed before the halftone update could run.");
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
                Dictionary<long, HalftoneCategoryChange> changes = ResolveChanges(document, request.Targets, out resolvedTargetCount);
                if (resolvedTargetCount != request.Targets.Count)
                {
                    Complete(request, false, "One or more DWGs are no longer available. Refresh and try again.");
                    return;
                }

                using (Transaction transaction = new Transaction(document, "Update DWG Halftone"))
                {
                    transaction.Start();
                    foreach (HalftoneCategoryChange change in changes.Values)
                    {
                        OverrideGraphicSettings settings = targetView.GetCategoryOverrides(change.Category.Id);
                        settings.SetHalftone(change.IsHalftone);
                        targetView.SetCategoryOverrides(change.Category.Id, settings);
                    }
                    transaction.Commit();
                }

                int count = changes.Count;
                Complete(request, true, $"Updated halftone for {count} {(count == 1 ? "DWG" : "DWGs")}.");
            }
            catch (Exception ex)
            {
                Complete(request, false, $"Failed to update halftone: {ex.Message}");
            }
        }

        private static Dictionary<long, HalftoneCategoryChange> ResolveChanges(
            Document document,
            IEnumerable<HalftoneChangeTarget> targets,
            out int resolvedTargetCount)
        {
            Dictionary<long, HalftoneCategoryChange> changes = new Dictionary<long, HalftoneCategoryChange>();
            resolvedTargetCount = 0;
            foreach (HalftoneChangeTarget target in targets)
            {
                ImportInstance importInstance = document.GetElement(target.ImportInstanceId) as ImportInstance;
                if (importInstance?.Category != null)
                {
                    changes[importInstance.Category.Id.GetIdValue()] =
                        new HalftoneCategoryChange(importInstance.Category, target.IsHalftone);
                    resolvedTargetCount++;
                }
            }
            return changes;
        }

        private void Complete(HalftoneRequest request, bool succeeded, string message)
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
            return "DWG Halftone Toggler";
        }

        private sealed class HalftoneCategoryChange
        {
            public HalftoneCategoryChange(Category category, bool isHalftone)
            {
                Category = category;
                IsHalftone = isHalftone;
            }

            public Category Category { get; }
            public bool IsHalftone { get; }
        }

        private sealed class HalftoneRequest
        {
            public HalftoneRequest(
                Document document,
                ElementId viewId,
                List<HalftoneChangeTarget> targets,
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
            public List<HalftoneChangeTarget> Targets { get; }
            public Action<string> OnComplete { get; }
            public Action<string> OnError { get; }
        }
    }
}
