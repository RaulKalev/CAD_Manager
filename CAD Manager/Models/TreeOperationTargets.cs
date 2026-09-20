using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Models
{
    public sealed class VisibilityChangeTarget
    {
        public VisibilityChangeTarget(ElementId importInstanceId, string layerName, bool isVisible)
        {
            ImportInstanceId = importInstanceId;
            LayerName = layerName;
            IsVisible = isVisible;
        }

        public ElementId ImportInstanceId { get; }
        public string LayerName { get; }
        public bool IsVisible { get; }
    }

    public sealed class HalftoneChangeTarget
    {
        public HalftoneChangeTarget(ElementId importInstanceId, bool isHalftone)
        {
            ImportInstanceId = importInstanceId;
            IsHalftone = isHalftone;
        }

        public ElementId ImportInstanceId { get; }
        public bool IsHalftone { get; }
    }

    public sealed class PresetLayerState
    {
        public PresetLayerState(
            string name,
            bool isVisible,
            string linePattern,
            string lineColor,
            int? lineWeight)
        {
            Name = name;
            IsVisible = isVisible;
            LinePattern = linePattern;
            LineColor = lineColor;
            LineWeight = lineWeight;
        }

        public string Name { get; }
        public bool IsVisible { get; }
        public string LinePattern { get; }
        public string LineColor { get; }
        public int? LineWeight { get; }
    }

    public sealed class PresetDwgState
    {
        public PresetDwgState(
            ElementId importInstanceId,
            string name,
            bool isVisible,
            bool isHalftone,
            string linePattern,
            string lineColor,
            int? lineWeight,
            IEnumerable<PresetLayerState> layers)
        {
            ImportInstanceId = importInstanceId;
            Name = name;
            IsVisible = isVisible;
            IsHalftone = isHalftone;
            LinePattern = linePattern;
            LineColor = lineColor;
            LineWeight = lineWeight;
            Layers = (layers ?? Enumerable.Empty<PresetLayerState>())
                .Where(layer => layer != null)
                .ToList()
                .AsReadOnly();
        }

        public ElementId ImportInstanceId { get; }
        public string Name { get; }
        public bool IsVisible { get; }
        public bool IsHalftone { get; }
        public string LinePattern { get; }
        public string LineColor { get; }
        public int? LineWeight { get; }
        public IReadOnlyList<PresetLayerState> Layers { get; }
    }
}
