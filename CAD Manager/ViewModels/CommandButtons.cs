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
        // Add new fields for the query event

        private readonly Window _owner;

        public CommandButtons(UIDocument uiDoc, ExternalEvent externalEvent, VisibilityToggler visibilityToggler, Action refreshTreeView, Window owner)
        {
            _uiDoc = uiDoc;
            _externalEvent = externalEvent;
            _visibilityToggler = visibilityToggler;
            _owner = owner;
            _layerVisibilityManager = new LayerVisibilityManager(_uiDoc.Document, _externalEvent, _visibilityToggler, _owner);
            _refreshTreeView = refreshTreeView;
        }
        public void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            string saveFolder = _layerVisibilityManager.GetProjectSaveFolder();
            if (string.IsNullOrEmpty(saveFolder))
            {
                UniversalPopupWindow.Show("Save folder not found. Save a template to create a save folder in your project directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
                return;
            }

            // ✅ Correct call
            string matchingTemplateFolder = _layerVisibilityManager.FindMatchingTemplate(saveFolder, _visibilityToggler.DWGNodes);

            if (string.IsNullOrEmpty(matchingTemplateFolder))
            {
                UniversalPopupWindow.Show("No matching template found.", "No Match", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
                return;
            }

            // ✅ Correct load
            _layerVisibilityManager.LoadLayerVisibility(matchingTemplateFolder, _visibilityToggler.DWGNodes);
            _refreshTreeView();
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
                _refreshTreeView();
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
                        _refreshTreeView();
                        isValidFile = true;
                    }
                    else
                    {
                        UniversalPopupWindow.Show(
                            "The selected file does not match the current layers. Please select a matching JSON file.",
                            "Invalid File",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error,
                            _owner);
                    }
                }
                else
                {
                    // User canceled the browse window
                    UniversalPopupWindow.Show("No file loaded.", "Info", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
                    break;
                }
            }
        }
        public void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            Document doc = _uiDoc.Document;
            View currentView = doc.ActiveView;

            // Recollect DWGNodes
            // Recollect DWGNodes
            var dataService = new DWGDataService();
            List<DWGNode> updatedDwgNodes = dataService.CollectDWGNodes(doc, currentView);

            // Update shared data
            _visibilityToggler.DWGNodes = updatedDwgNodes;

            // Replace the original DWGNodes list (used for UI filtering)
            if (_refreshTreeView.Target is CADManagerWindow window)
            {
                window.DWGNodes = updatedDwgNodes;
                window.FilteredDWGNodes = updatedDwgNodes;
            }

            // Refresh the TreeView UI
            _refreshTreeView.Invoke();
        }



    }
}
