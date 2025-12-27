using Autodesk.Revit.UI;
using System;
using CAD_Manager.Models;
using CAD_Manager.UI;
using CAD_Manager.Helpers;
using CAD_Manager; // For CADManagerWindow
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace CAD_Manager.ViewModels
{
    public class TreeViewControls
    {
        private readonly ExternalEvent _externalEvent;
        private readonly List<DWGNode> _dwgNodes;

        public TreeViewControls(ExternalEvent externalEvent, List<DWGNode> dwgNodes)
        {
            _externalEvent = externalEvent;
            _dwgNodes = dwgNodes;
        }

        /// <summary>
        /// Sorts DWG nodes and their layers alphabetically.
        /// </summary>
        public void SortDWGs(List<DWGNode> dwgNodes)
        {
            if (dwgNodes == null) return;

            dwgNodes.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var dwgNode in dwgNodes)
            {
                dwgNode.Layers = dwgNode.Layers.OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>
        /// Expands all DWG nodes by setting the IsExpanded property on the model.
        /// </summary>
        public void ExpandAllNodes(IEnumerable<DWGNode> nodes)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                node.IsExpanded = true;
            }
        }

        /// <summary>
        /// Handles the toggling of visibility checkboxes, separating DWG and Layer toggles, and applies changes to selected rows.
        /// </summary>
        public void HandleCheckBoxToggled(object sender, TreeView treeView)
        {
            if (sender is CheckBox checkBox)
            {
                bool isChecked = checkBox.IsChecked == true;

                // Find the TreeViewItem associated with the checkbox
                var treeViewItem = FindParentTreeViewItem(checkBox);
                if (treeViewItem == null) return;

                var dataContext = treeViewItem.DataContext;

                // Apply to all selected DWGNodes
                var selectedDwgNodes = GetSelectedDWGNodes().ToList();
                if (selectedDwgNodes.Any())
                {
                    foreach (var selectedDwg in selectedDwgNodes)
                    {
                        selectedDwg.IsChecked = isChecked;
                    }
                }

                // Apply to all selected LayerNodes
                var selectedLayerNodes = GetSelectedLayerNodes().ToList();
                if (selectedLayerNodes.Any())
                {
                    foreach (var selectedLayer in selectedLayerNodes)
                    {
                        selectedLayer.IsChecked = isChecked;
                    }
                }

                // If neither DWG nor Layer is selected, fallback to the clicked node
                if (!selectedDwgNodes.Any() && !selectedLayerNodes.Any())
                {
                    if (dataContext is DWGNode dwgNode)
                    {
                        dwgNode.IsChecked = isChecked;
                    }
                    else if (dataContext is LayerNode layerNode)
                    {
                        layerNode.IsChecked = isChecked;
                    }
                }

                // Trigger the external event after handling the toggle
                _externalEvent.Raise();

                // Force visual update of TreeView checkboxes
                RefreshCheckboxes(treeView);
            }
        }

        /// <summary>
        /// Finds the parent TreeViewItem of a control.
        /// </summary>
        private TreeViewItem FindParentTreeViewItem(DependencyObject child)
        {
            while (child != null && !(child is TreeViewItem))
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child as TreeViewItem;
        }


        private void RefreshCheckboxes(ItemsControl parent)
        {
            if (parent == null) return;

            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
                {
                    // Find the CheckBox in the TreeViewItem
                    var checkBox = FindVisualChild<CheckBox>(treeViewItem);
                    if (checkBox != null)
                    {
                        checkBox.IsChecked = (item as DWGNode)?.IsChecked ?? (item as LayerNode)?.IsChecked;
                    }

                    // Recursively refresh child items
                    if (treeViewItem.HasItems)
                    {
                        RefreshCheckboxes(treeViewItem);
                    }
                }
            }
        }

        // Helper method to find a child of a specific type in the visual tree
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        /// <summary>
        /// Retrieves all selected DWGNodes.
        /// </summary>
        private IEnumerable<DWGNode> GetSelectedDWGNodes()
        {
            return _dwgNodes?.Where(node => node.IsSelected) ?? Enumerable.Empty<DWGNode>();
        }

        /// <summary>
        /// Retrieves all selected LayerNodes.
        /// </summary>
        private IEnumerable<LayerNode> GetSelectedLayerNodes()
        {
            return _dwgNodes?
                .SelectMany(dwg => dwg.Layers)
                .Where(layer => layer.IsSelected)
                ?? Enumerable.Empty<LayerNode>();
        }

        /// <summary>
        /// Refreshes the TreeView with the updated data.
        /// </summary>
        public void RefreshTreeView(TreeView treeView, List<DWGNode> filteredNodes)
        {
            if (treeView == null || filteredNodes == null) return;

            treeView.ItemsSource = null;
            treeView.ItemsSource = filteredNodes;
            // ExpandAllNodes(filteredNodes); // Do not force expansion on refresh

            // Ensure selection is visually reflected
            ApplySelectionToTreeView(treeView, filteredNodes);
        }

        /// <summary>
        /// Applies selection state to TreeView items based on DWGNode's IsSelected property.
        /// </summary>
        private void ApplySelectionToTreeView(TreeView treeView, List<DWGNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (treeView.ItemContainerGenerator.ContainerFromItem(node) is TreeViewItem treeViewItem)
                {
                    treeViewItem.IsSelected = node.IsSelected;
                }

                // Handle child layers
                if (node.Layers != null && node.Layers.Any())
                {
                    foreach (var layer in node.Layers)
                    {
                        if (treeView.ItemContainerGenerator.ContainerFromItem(layer) is TreeViewItem layerItem)
                        {
                            layerItem.IsSelected = layer.IsSelected;
                        }
                    }
                }
            }
        }

    }
}
