using CAD_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace CAD_Manager.ViewModels
{
    public sealed class TreeVisibilityChange
    {
        public TreeVisibilityChange(object node, bool previousValue, bool newValue)
        {
            Node = node;
            PreviousValue = previousValue;
            NewValue = newValue;
        }

        public object Node { get; }
        public bool PreviousValue { get; }
        public bool NewValue { get; }
    }

    public class TreeViewControls
    {
        /// <summary>
        /// Sorts DWG nodes and their layers alphabetically.
        /// </summary>
        public void SortDWGs(List<DWGNode> dwgNodes)
        {
            if (dwgNodes == null)
                return;

            dwgNodes.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            foreach (DWGNode dwgNode in dwgNodes)
            {
                dwgNode.Layers = dwgNode.Layers
                    .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public void ExpandAllNodes(IEnumerable<DWGNode> nodes)
        {
            if (nodes == null)
                return;

            foreach (DWGNode node in nodes)
                node.IsExpanded = true;
        }

        /// <summary>
        /// Applies an immediate visibility preview and returns enough state to roll it back.
        /// Multi-row application occurs only when the clicked row is itself selected.
        /// </summary>
        public List<TreeVisibilityChange> PrepareVisibilityChanges(
            CheckBox checkBox,
            List<DWGNode> currentFilteredList)
        {
            List<TreeVisibilityChange> changes = new List<TreeVisibilityChange>();
            if (checkBox == null || currentFilteredList == null)
                return changes;

            object clickedNode = checkBox.DataContext;
            bool newValue = checkBox.IsChecked == true;
            bool clickedIsSelected = IsSelected(clickedNode);

            List<object> targets = clickedIsSelected
                ? GetSelectedNodes(currentFilteredList).ToList()
                : new List<object> { clickedNode };

            foreach (object target in targets.Where(target => target is DWGNode || target is LayerNode).Distinct())
            {
                bool previousValue = ReferenceEquals(target, clickedNode)
                    ? !newValue
                    : GetVisibility(target);

                SetVisibility(target, newValue);
                changes.Add(new TreeVisibilityChange(target, previousValue, newValue));
            }

            return changes;
        }

        public void RefreshTreeView(TreeView treeView, List<DWGNode> filteredNodes)
        {
            if (treeView == null || filteredNodes == null)
                return;

            // Preserve realized containers, keyboard focus, expansion, and scroll position
            // when only row properties changed.
            if (!ReferenceEquals(treeView.ItemsSource, filteredNodes))
                treeView.ItemsSource = filteredNodes;
        }

        private static IEnumerable<object> GetSelectedNodes(IEnumerable<DWGNode> nodes)
        {
            foreach (DWGNode dwgNode in nodes)
            {
                if (dwgNode.IsSelected)
                    yield return dwgNode;

                foreach (LayerNode layer in dwgNode.Layers.Where(item => item.IsSelected))
                    yield return layer;
            }
        }

        private static bool IsSelected(object node)
        {
            if (node is DWGNode dwgNode)
                return dwgNode.IsSelected;
            if (node is LayerNode layerNode)
                return layerNode.IsSelected;
            return false;
        }

        private static bool GetVisibility(object node)
        {
            if (node is DWGNode dwgNode)
                return dwgNode.IsChecked;
            return node is LayerNode layerNode && layerNode.IsChecked;
        }

        private static void SetVisibility(object node, bool value)
        {
            if (node is DWGNode dwgNode)
                dwgNode.IsChecked = value;
            else if (node is LayerNode layerNode)
                layerNode.IsChecked = value;
        }
    }
}
