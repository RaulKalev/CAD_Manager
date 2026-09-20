using System;
using CAD_Manager.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CAD_Manager.Helpers
{
    public static class Search
    {
        /// <summary>
        /// Filters DWG nodes and their layers based on the search query.
        /// </summary>
        public static List<DWGNode> FilterDWGNodes(List<DWGNode> dwgNodes, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return dwgNodes;

            query = query.Normalize(NormalizationForm.FormKC);

            return dwgNodes
                .Where(dwg =>
                    (dwg.Name ?? string.Empty).Normalize(NormalizationForm.FormKC)
                        .IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                    ||
                    dwg.Layers.Any(layer =>
                        (layer.Name ?? string.Empty).Normalize(NormalizationForm.FormKC)
                            .IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .Select(dwg => new DWGNode
                {
                    Name = dwg.Name,
                    IsChecked = dwg.IsChecked,
                    ElementId = dwg.ElementId,
                    IsHalftone = dwg.IsHalftone,
                    LineColor = dwg.LineColor,
                    LinePattern = dwg.LinePattern,
                    LineWeight = dwg.LineWeight,
                    IsSelected = dwg.IsSelected,
                    IsVisibilityPending = dwg.IsVisibilityPending,
                    IsHalftonePending = dwg.IsHalftonePending,
                    OperationError = dwg.OperationError,
                    Layers = dwg.Layers
                        .Where(layer =>
                            (layer.Name ?? string.Empty).Normalize(NormalizationForm.FormKC)
                                .IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        .ToList()
                })
                .ToList();
        }
    }


}
