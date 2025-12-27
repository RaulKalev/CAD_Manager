using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using CAD_Manager.Models;
using CAD_Manager.Handlers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

using CAD_Manager.UI;

namespace CAD_Manager.Services
{
    public class LayerVisibilityManager
    {
        private readonly Document _document;
        private readonly ExternalEvent _externalEvent;
        private readonly VisibilityToggler _visibilityToggler;

        private readonly Window _owner;

        public LayerVisibilityManager(Document document, ExternalEvent externalEvent, VisibilityToggler visibilityToggler, Window owner)
        {
            _document = document;
            _externalEvent = externalEvent;
            _visibilityToggler = visibilityToggler;
            _owner = owner;
        }

        private class LayerVisibilityData
        {
            public bool Visibility { get; set; }
            public bool? Halftone { get; set; }
            
            // DWG-level overrides
            public string LinePattern { get; set; }
            public string LineColor { get; set; }
            public int? LineWeight { get; set; }

            // Value handles both bool (legacy) and LayerData (new) via custom deserialization
            public Dictionary<string, object> Layers { get; set; }
        }

        private class LayerData
        {
            public bool Visibility { get; set; }
            public string LinePattern { get; set; }
            public string LineColor { get; set; }
            public int? LineWeight { get; set; }
        }

        public string GetProjectSaveFolder()
        {
            try
            {
                string path = TryGetActualFilePath(_document);

                if (!string.IsNullOrEmpty(path))
                {
                    string projectDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(projectDir))
                    {
                        string saveFolder = Path.Combine(projectDir, "LayerToggles");
                        Directory.CreateDirectory(saveFolder); // Ensure the folder exists
                        return saveFolder;
                    }
                }

                // Fallback: use AppData if path was null or directory doesn't exist
                string projectName = _document.Title ?? "UnknownProject";
                string fallbackDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RK Tools", "CADManager", "LayerToggles", SanitizeFolderName(projectName));

                Directory.CreateDirectory(fallbackDir);
                return fallbackDir;
            }
            catch (Exception ex)
            {
                UniversalPopupWindow.Show($"Error accessing save folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
                return null;
            }
        }
        private string TryGetActualFilePath(Document doc)
        {
            try
            {
                // Prefer local path if available
                if (!string.IsNullOrWhiteSpace(doc.PathName) && File.Exists(doc.PathName))
                {
                    return doc.PathName;
                }

                // Check for a workshared central model path (e.g. Desktop Connector)
                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                if (modelPath != null)
                {
                    string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                    if (!string.IsNullOrEmpty(userVisiblePath) && File.Exists(userVisiblePath))
                    {
                        return userVisiblePath;
                    }
                }
            }
            catch
            {
                // Ignore and return null
            }

            return null;
        }

        private string SanitizeFolderName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
        public void SaveLayerVisibility(string viewName, List<DWGNode> dwgNodes)
        {
            try
            {
                string saveFolder = GetProjectSaveFolder();
                if (saveFolder == null) return;

                foreach (var dwgNode in dwgNodes)
                {
                    var data = new LayerVisibilityData
                    {
                        Visibility = dwgNode.IsChecked,
                        Halftone = dwgNode.IsHalftone,
                        LinePattern = dwgNode.LinePattern,
                        LineColor = dwgNode.LineColor,
                        LineWeight = dwgNode.LineWeight,
                        Layers = new Dictionary<string, object>()
                    };

                    foreach (var layer in dwgNode.Layers)
                    {
                        string key = (layer.Name ?? string.Empty).Normalize(NormalizationForm.FormKC);
                        
                        // Save as rich object
                        data.Layers[key] = new LayerData
                        {
                            Visibility = layer.IsChecked,
                            LinePattern = layer.LinePattern,
                            LineColor = layer.LineColor,
                            LineWeight = layer.LineWeight
                        };
                    }

                    string sanitizedDwgName = SanitizeFileName((dwgNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC));
                    string filePath = Path.Combine(saveFolder, $"{sanitizedDwgName}.json");

                    var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                }

                UniversalPopupWindow.Show($"Layer visibility saved to folder:\n{saveFolder}", "Success", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
            }
            catch (Exception ex)
            {
                UniversalPopupWindow.Show($"Error saving layer visibility: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
            }
        }
        public void LoadLayerVisibility(string folderPath, List<DWGNode> dwgNodes)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    UniversalPopupWindow.Show($"No saved Data found at: {folderPath}", "Info", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
                    return;
                }

                foreach (var dwgNode in dwgNodes)
                {
                    string sanitizedDwgName = SanitizeFileName((dwgNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC));
                    string filePath = Path.Combine(folderPath, $"{sanitizedDwgName}.json");

                    if (File.Exists(filePath))
                    {
                        var savedJson = File.ReadAllText(filePath, Encoding.UTF8);
                        var savedData = JsonConvert.DeserializeObject<LayerVisibilityData>(savedJson);

                        dwgNode.IsChecked = savedData.Visibility;
                        dwgNode.IsHalftone = savedData.Halftone ?? false;
                        
                        // Load DWG Overrides
                        dwgNode.LinePattern = savedData.LinePattern;
                        dwgNode.LineColor = savedData.LineColor;
                        dwgNode.LineWeight = savedData.LineWeight;

                        if (savedData.Layers != null)
                        {
                            foreach (var layerNode in dwgNode.Layers)
                            {
                                var key = (layerNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC);
                                if (savedData.Layers.TryGetValue(key, out object rawValue))
                                {
                                    if (rawValue is bool boolVis)
                                    {
                                        // Legacy Format: just visibility boolean
                                        layerNode.IsChecked = boolVis;
                                        // Implicitly clears overrides as they remain null
                                    }
                                    else if (rawValue != null)
                                    {
                                        // Attempt to convert to LayerData (JObject or similar)
                                        try 
                                        {
                                            // Provide robust conversion from JObject
                                            var layerJson = JsonConvert.SerializeObject(rawValue);
                                            var layerData = JsonConvert.DeserializeObject<LayerData>(layerJson);
                                            
                                            if (layerData != null)
                                            {
                                                layerNode.IsChecked = layerData.Visibility;
                                                layerNode.LinePattern = layerData.LinePattern;
                                                layerNode.LineColor = layerData.LineColor;
                                                layerNode.LineWeight = layerData.LineWeight;
                                            }
                                        }
                                        catch
                                        {
                                            // Fallback or ignore
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                dwgNodes.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
                foreach (var dwgNode in dwgNodes)
                {
                    dwgNode.Layers = dwgNode.Layers.OrderBy(layer => layer.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }

                _visibilityToggler.DWGNodes = dwgNodes;
                _externalEvent.Raise();

                // If we loaded overrides, we might want to ensure they apply. 
                // The VisibilityToggler executes DWGVisibilityController, which we will update to handle overrides using the properties we just populated.

                UniversalPopupWindow.Show("Layer visibility and overrides loaded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information, _owner);
            }
            catch (Exception ex)
            {
                UniversalPopupWindow.Show($"Error loading layer visibility: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
            }
        }
        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        public string FindMatchingTemplate(string folderPath, List<DWGNode> dwgNodes)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return null;

                foreach (var dwgNode in dwgNodes)
                {
                    string sanitizedDwgName = SanitizeFileName(dwgNode.Name);
                    string filePath = Path.Combine(folderPath, $"{sanitizedDwgName}.json");
                    if (File.Exists(filePath))
                    {
                        return folderPath; // ✅ Return the folder, not the file
                    }
                }
            }
            catch (Exception ex)
            {
                UniversalPopupWindow.Show($"Error searching for matching template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error, _owner);
            }

            return null;
        }

        public List<string> FindAllMatchingTemplates(string folderPath, List<DWGNode> dwgNodes)
        {
            var matchingFiles = new List<string>();

            if (!Directory.Exists(folderPath))
                return matchingFiles;

            foreach (var file in Directory.GetFiles(folderPath, "*.json"))
            {
                if (DoesFileMatch(file, dwgNodes))
                {
                    matchingFiles.Add(file);
                    Console.WriteLine($"Matching file found: {file}"); // Debugging line
                }
            }

            return matchingFiles;
        }
        public bool DoesFileMatch(string folderPath, List<DWGNode> dwgNodes)
        {
            try
            {
                foreach (var dwgNode in dwgNodes)
                {
                    string sanitizedDwgName = SanitizeFileName((dwgNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC));
                    string filePath = Path.Combine(folderPath, $"{sanitizedDwgName}.json");
                    if (File.Exists(filePath))
                    {
                        return true; // At least one matching DWG file exists
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DoesFileMatch: {ex.Message}");
            }

            return false;
        }

    }
}
