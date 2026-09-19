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
    public sealed class ColorOverrideHandler : IExternalEventHandler
    {
        private readonly object _requestLock = new object();
        private ColorOverrideRequest _pendingRequest;

        public bool TrySubmit(
            Document document,
            ElementId viewId,
            IEnumerable<LineGraphicsTarget> targets,
            bool isLayerOverride,
            LineGraphicsColor overrideColor,
            string linePattern,
            int? lineWeight,
            bool clearOverrides,
            Action<string> onComplete,
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

                _pendingRequest = new ColorOverrideRequest(
                    document,
                    viewId,
                    targetList,
                    isLayerOverride,
                    overrideColor,
                    linePattern,
                    lineWeight,
                    clearOverrides,
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
            ColorOverrideRequest request;
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
                Complete(request, false, "The document changed before the graphics update could run.");
                return;
            }

            if (activeView == null || activeView.Id.GetIdValue() != request.ViewId.GetIdValue())
            {
                Complete(request, false, "The active view changed. Reopen Line Graphics for the current view.");
                return;
            }

            try
            {
                View viewToModify = ResolveViewToModify(document, activeView);
                List<Category> targetCategories = ResolveTargetCategories(document, request);
                if (targetCategories.Count == 0)
                {
                    Complete(request, false, "The selected DWG or layers are no longer available.");
                    return;
                }

                ElementId patternId = ResolvePatternId(document, request.LinePattern);
                if (!request.ClearOverrides &&
                    !string.IsNullOrEmpty(request.LinePattern) &&
                    request.LinePattern != "<No Override>" &&
                    patternId == ElementId.InvalidElementId)
                {
                    Complete(request, false, $"The line pattern “{request.LinePattern}” is no longer available.");
                    return;
                }

                using (Transaction transaction = new Transaction(document, "Apply DWG and Layer Graphics"))
                {
                    transaction.Start();
                    foreach (Category category in targetCategories)
                    {
                        OverrideGraphicSettings settings = request.ClearOverrides
                            ? new OverrideGraphicSettings()
                            : viewToModify.GetCategoryOverrides(category.Id);

                        if (!request.ClearOverrides)
                            ApplyChanges(settings, request, patternId);

                        viewToModify.SetCategoryOverrides(category.Id, settings);
                    }

                    transaction.Commit();
                }

                string scope = request.IsLayerOverride ? "layer" : "DWG";
                string noun = targetCategories.Count == 1 ? scope : scope + "s";
                string verb = request.ClearOverrides ? "Cleared overrides for" : "Updated";
                Complete(request, true, $"{verb} {targetCategories.Count} {noun}.");
            }
            catch (Exception ex)
            {
                Complete(request, false, $"Failed to apply line graphics: {ex.Message}");
            }
        }

        private static View ResolveViewToModify(Document document, View activeView)
        {
            if (activeView.ViewTemplateId == ElementId.InvalidElementId)
                return activeView;

            return document.GetElement(activeView.ViewTemplateId) as View ?? activeView;
        }

        private static List<Category> ResolveTargetCategories(Document document, ColorOverrideRequest request)
        {
            Dictionary<long, Category> categories = new Dictionary<long, Category>();
            foreach (LineGraphicsTarget target in request.Targets)
            {
                ImportInstance importInstance = document.GetElement(target.ImportInstanceId) as ImportInstance;
                if (importInstance?.Category == null)
                    continue;

                if (!request.IsLayerOverride)
                {
                    categories[importInstance.Category.Id.GetIdValue()] = importInstance.Category;
                    continue;
                }

                foreach (string layerName in target.LayerNames)
                {
                    Category category = FindLayerCategory(importInstance, layerName);
                    if (category != null)
                        categories[category.Id.GetIdValue()] = category;
                }
            }

            return categories.Values.ToList();
        }

        private static void ApplyChanges(
            OverrideGraphicSettings settings,
            ColorOverrideRequest request,
            ElementId patternId)
        {
            if (request.OverrideColor != null)
            {
                settings.SetProjectionLineColor(new Autodesk.Revit.DB.Color(
                    request.OverrideColor.Red,
                    request.OverrideColor.Green,
                    request.OverrideColor.Blue));
            }

            if (request.LinePattern == "<No Override>")
                settings.SetProjectionLinePatternId(ElementId.InvalidElementId);
            else if (!string.IsNullOrEmpty(request.LinePattern))
                settings.SetProjectionLinePatternId(patternId);

            if (request.LineWeight == -1)
                settings.SetProjectionLineWeight(OverrideGraphicSettings.InvalidPenNumber);
            else if (request.LineWeight > 0)
                settings.SetProjectionLineWeight(request.LineWeight.Value);
        }

        private static ElementId ResolvePatternId(Document document, string patternName)
        {
            if (string.IsNullOrEmpty(patternName) || patternName == "<No Override>")
                return ElementId.InvalidElementId;

            if (string.Equals(patternName, "Solid", StringComparison.OrdinalIgnoreCase))
                return LinePatternElement.GetSolidPatternId();

            LinePatternElement pattern = new FilteredElementCollector(document)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(item => string.Equals(item.Name, patternName, StringComparison.OrdinalIgnoreCase));
            return pattern?.Id ?? ElementId.InvalidElementId;
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

        private void Complete(ColorOverrideRequest request, bool succeeded, string message)
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
            return "Color Override Handler";
        }

        private sealed class ColorOverrideRequest
        {
            public ColorOverrideRequest(
                Document document,
                ElementId viewId,
                List<LineGraphicsTarget> targets,
                bool isLayerOverride,
                LineGraphicsColor overrideColor,
                string linePattern,
                int? lineWeight,
                bool clearOverrides,
                Action<string> onComplete,
                Action<string> onError)
            {
                Document = document;
                ViewId = viewId;
                Targets = targets;
                IsLayerOverride = isLayerOverride;
                OverrideColor = overrideColor;
                LinePattern = linePattern;
                LineWeight = lineWeight;
                ClearOverrides = clearOverrides;
                OnComplete = onComplete;
                OnError = onError;
            }

            public Document Document { get; }
            public ElementId ViewId { get; }
            public List<LineGraphicsTarget> Targets { get; }
            public bool IsLayerOverride { get; }
            public LineGraphicsColor OverrideColor { get; }
            public string LinePattern { get; }
            public int? LineWeight { get; }
            public bool ClearOverrides { get; }
            public Action<string> OnComplete { get; }
            public Action<string> OnError { get; }
        }
    }
}
