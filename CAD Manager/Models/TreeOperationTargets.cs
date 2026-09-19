using Autodesk.Revit.DB;

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
}
