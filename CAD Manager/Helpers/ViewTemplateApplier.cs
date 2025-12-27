using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CAD_Manager.Helpers
{
    public static class ViewTemplateApplier
    {
        /// <summary>
        /// Temporarily disables view template property enforcement.
        /// </summary>
        public static void DisableTemplateEnforcement(Document doc, View view)
        {
            if (view.ViewTemplateId == ElementId.InvalidElementId)
                return;

            View templateView = doc.GetElement(view.ViewTemplateId) as View;

            if (templateView == null)
                return;

            Parameter param = templateView.LookupParameter("Enable Temporary View Properties");
            if (param != null && param.AsInteger() == 0)
            {
                param.Set(1); // Enable temporary properties
            }
        }

        /// <summary>
        /// Restores the original state of view template property enforcement.
        /// </summary>
        public static void RestoreTemplateEnforcement(Document doc, View view)
        {
            if (view.ViewTemplateId == ElementId.InvalidElementId)
                return;

            View templateView = doc.GetElement(view.ViewTemplateId) as View;

            if (templateView == null)
                return;

            Parameter param = templateView.LookupParameter("Enable Temporary View Properties");
            if (param != null && param.AsInteger() == 1)
            {
                param.Set(0); // Restore template enforcement
            }
        }
    }
}
