using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD_Manager.Helpers;
using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CAD_Manager.Handlers
{
    public sealed class LineGraphicsReadHandler : IExternalEventHandler
    {
        private readonly object _requestLock = new object();
        private ReadRequest _pendingRequest;

        public bool TrySubmit(
            Document document,
            ElementId viewId,
            IEnumerable<LineGraphicsTarget> targets,
            bool isLayerOverride,
            Action<LineGraphicsSnapshot> onComplete,
            Action<string> onError)
        {
            List<LineGraphicsTarget> targetList = (targets ?? Enumerable.Empty<LineGraphicsTarget>())
                .Where(target => target != null && target.ImportInstanceId != null && target.ImportInstanceId != ElementId.InvalidElementId)
                .ToList();

            if (document == null || viewId == null || viewId == ElementId.InvalidElementId || targetList.Count == 0)
                return false;

            lock (_requestLock)
            {
                if (_pendingRequest != null)
                    return false;

                _pendingRequest = new ReadRequest(document, viewId, targetList, isLayerOverride, onComplete, onError);
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
            ReadRequest request;
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
                Complete(request, null, "The document changed before the graphics inspector could load.");
                return;
            }

            if (activeView == null || activeView.Id.GetIdValue() != request.ViewId.GetIdValue())
            {
                Complete(request, null, "The active view changed. Reopen Line Graphics for the current view.");
                return;
            }

            try
            {
                View viewToInspect = ResolveViewToModify(document, activeView);
                List<OverrideGraphicSettings> overrides = ResolveOverrides(document, viewToInspect, request);
                if (overrides.Count == 0)
                {
                    Complete(request, null, "The selected DWG or layers are no longer available.");
                    return;
                }

                List<string> patternNames = new FilteredElementCollector(document)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .Select(pattern => pattern.Name)
                    .Where(name => !string.Equals(name, "Solid", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name)
                    .ToList();
                patternNames.Insert(0, "Solid");

                LineGraphicsSnapshot snapshot = CreateSnapshot(document, overrides, patternNames);
                Complete(request, snapshot, null);
            }
            catch (Exception ex)
            {
                Complete(request, null, $"Could not load line graphics: {ex.Message}");
            }
        }

        private static View ResolveViewToModify(Document document, View activeView)
        {
            if (activeView.ViewTemplateId == ElementId.InvalidElementId)
                return activeView;

            return document.GetElement(activeView.ViewTemplateId) as View ?? activeView;
        }

        private static List<OverrideGraphicSettings> ResolveOverrides(Document document, View view, ReadRequest request)
        {
            List<OverrideGraphicSettings> results = new List<OverrideGraphicSettings>();
            foreach (LineGraphicsTarget target in request.Targets)
            {
                ImportInstance importInstance = document.GetElement(target.ImportInstanceId) as ImportInstance;
                if (importInstance?.Category == null)
                    continue;

                if (!request.IsLayerOverride)
                {
                    results.Add(view.GetCategoryOverrides(importInstance.Category.Id));
                    continue;
                }

                foreach (string layerName in target.LayerNames)
                {
                    Category category = FindLayerCategory(importInstance, layerName);
                    if (category != null)
                        results.Add(view.GetCategoryOverrides(category.Id));
                }
            }

            return results;
        }

        private static LineGraphicsSnapshot CreateSnapshot(
            Document document,
            IList<OverrideGraphicSettings> overrides,
            IEnumerable<string> patternNames)
        {
            Autodesk.Revit.DB.Color firstColor = null;
            ElementId firstPatternId = null;
            int? firstWeight = null;
            bool colorVaries = false;
            bool patternVaries = false;
            bool weightVaries = false;

            for (int index = 0; index < overrides.Count; index++)
            {
                OverrideGraphicSettings settings = overrides[index];
                Autodesk.Revit.DB.Color color = settings.ProjectionLineColor;
                if (color != null && !color.IsValid)
                    color = null;

                ElementId patternId = settings.ProjectionLinePatternId;
                if (patternId == ElementId.InvalidElementId)
                    patternId = null;

                int? weight = settings.ProjectionLineWeight > 0
                    ? (int?)settings.ProjectionLineWeight
                    : null;

                if (index == 0)
                {
                    firstColor = color;
                    firstPatternId = patternId;
                    firstWeight = weight;
                    continue;
                }

                colorVaries = colorVaries || !ColorsEqual(firstColor, color);
                patternVaries = patternVaries || !IdsEqual(firstPatternId, patternId);
                weightVaries = weightVaries || firstWeight != weight;
            }

            string pattern = null;
            if (!patternVaries && firstPatternId != null)
            {
                pattern = firstPatternId.GetIdValue() == LinePatternElement.GetSolidPatternId().GetIdValue()
                    ? "Solid"
                    : (document.GetElement(firstPatternId) as LinePatternElement)?.Name;
            }

            LineGraphicsColor colorValue = firstColor == null || colorVaries
                ? null
                : new LineGraphicsColor(firstColor.Red, firstColor.Green, firstColor.Blue);

            return new LineGraphicsSnapshot(
                patternNames,
                colorValue,
                pattern,
                firstWeight,
                colorVaries,
                patternVaries,
                weightVaries);
        }

        private static bool ColorsEqual(Autodesk.Revit.DB.Color first, Autodesk.Revit.DB.Color second)
        {
            if (first == null || second == null)
                return first == null && second == null;

            return first.Red == second.Red && first.Green == second.Green && first.Blue == second.Blue;
        }

        private static bool IdsEqual(ElementId first, ElementId second)
        {
            if (first == null || second == null)
                return first == null && second == null;

            return first.GetIdValue() == second.GetIdValue();
        }

        private static Category FindLayerCategory(ImportInstance importInstance, string layerName)
        {
            string key = layerName?.Normalize(NormalizationForm.FormKC);
            return importInstance.Category?.SubCategories?
                .Cast<Category>()
                .FirstOrDefault(category => string.Equals(
                    category.Name?.Normalize(NormalizationForm.FormKC),
                    key,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        private void Complete(ReadRequest request, LineGraphicsSnapshot snapshot, string error)
        {
            lock (_requestLock)
            {
                if (!ReferenceEquals(_pendingRequest, request))
                    return;

                _pendingRequest = null;
            }

            if (error == null)
                request.OnComplete?.Invoke(snapshot);
            else
                request.OnError?.Invoke(error);
        }

        public string GetName()
        {
            return "Load Line Graphics State";
        }

        private sealed class ReadRequest
        {
            public ReadRequest(
                Document document,
                ElementId viewId,
                List<LineGraphicsTarget> targets,
                bool isLayerOverride,
                Action<LineGraphicsSnapshot> onComplete,
                Action<string> onError)
            {
                Document = document;
                ViewId = viewId;
                Targets = targets;
                IsLayerOverride = isLayerOverride;
                OnComplete = onComplete;
                OnError = onError;
            }

            public Document Document { get; }
            public ElementId ViewId { get; }
            public List<LineGraphicsTarget> Targets { get; }
            public bool IsLayerOverride { get; }
            public Action<LineGraphicsSnapshot> OnComplete { get; }
            public Action<string> OnError { get; }
        }
    }
}
