using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using CAD_Manager.Models;
using CAD_Manager.Services;
using CAD_Manager.Handlers;
using CAD_Manager.Helpers;
using CAD_Manager.UI;
using CAD_Manager; // For CADManagerWindow
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace CAD_Manager.ViewModels
{
    public class CommandButtons
    {
        private readonly UIDocument _uiDoc;
        private readonly ExternalEvent _externalEvent;
        private readonly VisibilityToggler _visibilityToggler;
        private readonly LayerVisibilityManager _layerVisibilityManager;
        private readonly Action _refreshTreeView;
        private readonly Window _owner;
        private readonly Action<string, bool, bool> _notify;

        public CommandButtons(
            UIDocument uiDoc,
            ExternalEvent externalEvent,
            VisibilityToggler visibilityToggler,
            Action refreshTreeView,
            Window owner,
            Action<string, bool, bool> notify,
            Action<IReadOnlyList<PresetDwgState>> applyPresetToUi)
        {
            _uiDoc = uiDoc;
            _externalEvent = externalEvent;
            _visibilityToggler = visibilityToggler;
            _owner = owner;
            _notify = notify;
            _layerVisibilityManager = new LayerVisibilityManager(
                _uiDoc.Document,
                _externalEvent,
                _visibilityToggler,
                _owner,
                _notify,
                applyPresetToUi);
            _refreshTreeView = refreshTreeView;
        }
        public void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            string saveFolder = _layerVisibilityManager.GetProjectSaveFolder();
            if (string.IsNullOrEmpty(saveFolder))
            {
                Notify("Save folder not found. Save a template to create one for this project.", true, false);
                return;
            }

            // ✅ Correct call
            string matchingTemplateFolder = _layerVisibilityManager.FindMatchingTemplate(saveFolder, _visibilityToggler.DWGNodes);

            if (string.IsNullOrEmpty(matchingTemplateFolder))
            {
                Notify("No matching layer template was found.", false, true);
                return;
            }

            // ✅ Correct load
            _layerVisibilityManager.LoadLayerVisibility(matchingTemplateFolder, _visibilityToggler.DWGNodes);
        }

        public void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Refresh override data from the current view before saving
            Document doc = _uiDoc.Document;
            View currentView = doc.ActiveView;
            
            // Use DWGDataService to collect fresh data with current overrides
            var dataService = new DWGDataService();
            List<DWGNode> currentNodes = dataService.CollectDWGNodes(doc, currentView);
            
            // Merge visibility states from UI with fresh override data
            foreach (var currentNode in currentNodes)
            {
                var existingNode = _visibilityToggler.DWGNodes.FirstOrDefault(n => n.Name == currentNode.Name);
                if (existingNode != null)
                {
                    // Preserve the visibility states from the UI
                    currentNode.IsChecked = existingNode.IsChecked;
                    currentNode.IsHalftone = existingNode.IsHalftone;
                    
                    // Merge layer visibility states
                    foreach (var currentLayer in currentNode.Layers)
                    {
                        var existingLayer = existingNode.Layers.FirstOrDefault(l => l.Name == currentLayer.Name);
                        if (existingLayer != null)
                        {
                            currentLayer.IsChecked = existingLayer.IsChecked;
                        }
                    }
                }
            }
            
            // Save with fresh override data
            _layerVisibilityManager.SaveLayerVisibility(currentView.Name, currentNodes);
        }

        public void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Select a JSON File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string folderPath = Path.GetDirectoryName(filePath); // ✅ get folder

                _layerVisibilityManager.LoadLayerVisibility(folderPath, _visibilityToggler.DWGNodes);
            }
        }


        public void OpenBrowseWindow()
        {
            bool isValidFile = false;

            while (!isValidFile)
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = "Select a JSON Template"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var selectedFile = openFileDialog.FileName;

                    if (_layerVisibilityManager.DoesFileMatch(selectedFile, _visibilityToggler.DWGNodes))
                    {
                        _layerVisibilityManager.LoadLayerVisibility(selectedFile, _visibilityToggler.DWGNodes);
                        isValidFile = true;
                    }
                    else
                    {
                        Notify("The selected file does not match the current layers. Choose a matching JSON file.", true, false);
                    }
                }
                else
                {
                    // Canceling the system picker is non-destructive and needs no alert.
                    break;
                }
            }
        }
        public void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Document doc = _uiDoc.Document;
            View currentView = doc.ActiveView;

            List<DWGNode> previousNodes = _visibilityToggler.DWGNodes ?? new List<DWGNode>();
            var dataService = new DWGDataService();
            List<DWGNode> updatedDwgNodes = dataService.CollectDWGNodes(doc, currentView);
            RestoreInteractionState(previousNodes, updatedDwgNodes);

            // Update shared data
            _visibilityToggler.DWGNodes = updatedDwgNodes;
            // Explicitly update context on the window to propagate to all handlers
            if (_refreshTreeView.Target is CADManagerWindow windowContext)
            {
                 windowContext.UpdateContext(doc, currentView);
            }
            else
            {
                // Fallback if target is not window (unlikely)
                _visibilityToggler.Document = doc;
                _visibilityToggler.CurrentView = currentView;
            }

            // Replace the original DWGNodes list (used for UI filtering)
            if (_refreshTreeView.Target is CADManagerWindow window)
            {
                window.ReplaceDWGNodes(updatedDwgNodes);
                return;
            }

            // Refresh the TreeView UI
            _refreshTreeView.Invoke();
        }

        public string GetLayerToggleFolder()
        {
            return _layerVisibilityManager.GetProjectSaveFolder();
        }

        public string GetDefaultLayerToggleFolder()
        {
            return _layerVisibilityManager.GetDefaultProjectSaveFolder();
        }

        public bool IsUsingCustomLayerToggleFolder()
        {
            return _layerVisibilityManager.IsUsingCustomProjectSaveFolder();
        }

        public int GetSavedLayerToggleCount()
        {
            return _layerVisibilityManager.GetSavedPresetCount();
        }

        public int ConfigureLayerToggleFolder(string customFolder, bool moveExistingFiles)
        {
            return _layerVisibilityManager.ConfigureProjectSaveFolder(customFolder, moveExistingFiles);
        }

        private static void RestoreInteractionState(
            IEnumerable<DWGNode> previousNodes,
            IEnumerable<DWGNode> updatedNodes)
        {
            List<DWGNode> previous = (previousNodes ?? Enumerable.Empty<DWGNode>()).ToList();
            foreach (DWGNode updated in updatedNodes ?? Enumerable.Empty<DWGNode>())
            {
                DWGNode prior = previous.FirstOrDefault(candidate => SameDwg(candidate, updated));
                if (prior == null)
                    continue;

                updated.IsSelected = prior.IsSelected;
                updated.IsExpanded = prior.IsExpanded;

                foreach (LayerNode updatedLayer in updated.Layers ?? new List<LayerNode>())
                {
                    LayerNode priorLayer = (prior.Layers ?? new List<LayerNode>()).FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, updatedLayer.Name, StringComparison.CurrentCultureIgnoreCase));
                    if (priorLayer != null)
                        updatedLayer.IsSelected = priorLayer.IsSelected;
                }
            }
        }

        private static bool SameDwg(DWGNode first, DWGNode second)
        {
            if (first?.ElementId != null && second?.ElementId != null)
                return first.ElementId.GetIdValue() == second.ElementId.GetIdValue();

            return string.Equals(first?.Name, second?.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        private void Notify(string message, bool isError, bool autoDismiss)
        {
            if (_notify != null)
            {
                _notify(message, isError, autoDismiss);
                return;
            }

            UniversalPopupWindow.Show(
                message,
                isError ? "Error" : "Notification",
                MessageBoxButton.OK,
                isError ? MessageBoxImage.Error : MessageBoxImage.Information,
                _owner);
        }



    }
}
