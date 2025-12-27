using System;
using CAD_Manager.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
                    Layers = dwg.Layers
                        .Where(layer =>
                            (layer.Name ?? string.Empty).Normalize(NormalizationForm.FormKC)
                                .IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        .ToList()
                })
                .ToList();
        }
        /// <summary>
        /// Handles the behavior when the search box gains focus.
        /// </summary>
        public static void HandleSearchBoxGotFocus(TextBox searchBox)
        {
            if (searchBox == null) return;

            searchBox.Text = string.Empty; // Clear the text
        }

        /// <summary>
        /// Handles the behavior when the search box loses focus.
        /// </summary>
        public static void HandleSearchBoxLostFocus(TextBox searchBox)
        {
            if (searchBox == null) return;

            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                searchBox.Text = "Search"; // Restore watermark text
            }
        }

        /// <summary>
        /// Clears the search box text.
        /// </summary>
        public static void ClearSearchBox(TextBox searchBox)
        {
            if (searchBox == null) return;

            searchBox.Text = string.Empty;
        }

        /// <summary>
        /// Handles text changes in the search box and filters the DWG nodes.
        /// </summary>
        public static void HandleSearchBoxTextChanged(TextBox searchBox, List<DWGNode> dwgNodes, Action<List<DWGNode>> updateTreeView)
        {
            if (searchBox == null || dwgNodes == null || updateTreeView == null) return;

            string query = searchBox.Text;

            // Filter DWG nodes and update the TreeView
            var filteredNodes = FilterDWGNodes(dwgNodes, query);
            updateTreeView(filteredNodes);
        }
    }


}
