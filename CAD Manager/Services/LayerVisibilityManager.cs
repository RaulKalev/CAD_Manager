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
        private readonly Action<string, bool, bool> _notify;
        private readonly Action<IReadOnlyList<PresetDwgState>> _applyPresetToUi;
        private readonly LayerToggleLocationStore _locationStore;

        public LayerVisibilityManager(
            Document document,
            ExternalEvent externalEvent,
            VisibilityToggler visibilityToggler,
            Window owner,
            Action<string, bool, bool> notify,
            Action<IReadOnlyList<PresetDwgState>> applyPresetToUi,
            LayerToggleLocationStore locationStore = null)
        {
            _document = document;
            _externalEvent = externalEvent;
            _visibilityToggler = visibilityToggler;
            _owner = owner;
            _notify = notify;
            _applyPresetToUi = applyPresetToUi;
            _locationStore = locationStore ?? new LayerToggleLocationStore();
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
                string customFolder = _locationStore.Get(GetProjectKey());
                if (!string.IsNullOrWhiteSpace(customFolder))
                {
                    Directory.CreateDirectory(customFolder);
                    return Path.GetFullPath(customFolder);
                }

                return GetDefaultProjectSaveFolder();
            }
            catch (Exception ex)
            {
                Notify($"Error accessing the save folder: {ex.Message}", true, false);
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

                Notify($"Layer visibility saved to {saveFolder}", false, true);
            }
            catch (Exception ex)
            {
                Notify($"Error saving layer visibility: {ex.Message}", true, false);
            }
        }
        public void LoadLayerVisibility(string folderPath, List<DWGNode> dwgNodes)
        {
            try
            {
                if (_visibilityToggler.HasPendingRequest)
                {
                    Notify("Another visibility update is already pending in Revit.", false, true);
                    return;
                }

                if (!Directory.Exists(folderPath))
                {
                    Notify($"No saved data was found at {folderPath}", false, true);
                    return;
                }

                int loadedFileCount = 0;
                List<PresetDwgState> states = new List<PresetDwgState>();
                foreach (DWGNode dwgNode in dwgNodes ?? new List<DWGNode>())
                {
                    string sanitizedDwgName = SanitizeFileName((dwgNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC));
                    string filePath = Path.Combine(folderPath, $"{sanitizedDwgName}.json");
                    LayerVisibilityData savedData = null;

                    if (File.Exists(filePath))
                    {
                        var savedJson = File.ReadAllText(filePath, Encoding.UTF8);
                        savedData = JsonConvert.DeserializeObject<LayerVisibilityData>(savedJson);
                        if (savedData != null)
                            loadedFileCount++;
                    }

                    List<PresetLayerState> layerStates = (dwgNode.Layers ?? new List<LayerNode>())
                        .Select(layerNode => CreatePresetLayerState(layerNode, savedData?.Layers))
                        .ToList();

                    states.Add(new PresetDwgState(
                        dwgNode.ElementId,
                        dwgNode.Name,
                        savedData?.Visibility ?? dwgNode.IsChecked,
                        savedData?.Halftone ?? dwgNode.IsHalftone,
                        savedData != null ? savedData.LinePattern : dwgNode.LinePattern,
                        savedData != null ? savedData.LineColor : dwgNode.LineColor,
                        savedData != null ? savedData.LineWeight : dwgNode.LineWeight,
                        layerStates));
                }

                if (loadedFileCount == 0)
                {
                    Notify("No matching layer preset files were found in the selected folder.", false, true);
                    return;
                }

                View activeView = _document.ActiveView;
                bool submitted = _visibilityToggler.TrySubmitPreset(
                    _document,
                    activeView?.Id,
                    states,
                    message =>
                    {
                        _applyPresetToUi?.Invoke(states);
                        Notify(message, false, true);
                    },
                    message => Notify(message, true, false));

                if (!submitted)
                {
                    Notify("The layer preset could not be queued. Refresh and try again.", true, false);
                    return;
                }

                ExternalEventRequest raiseResult = _externalEvent.Raise();
                if (raiseResult != ExternalEventRequest.Accepted)
                {
                    _visibilityToggler.CancelPendingRequest();
                    Notify("Revit could not queue the layer preset. Try again when Revit is idle.", true, false);
                }
            }
            catch (Exception ex)
            {
                Notify($"Error loading layer visibility: {ex.Message}", true, false);
            }
        }

        public string GetDefaultProjectSaveFolder()
        {
            string path = TryGetActualFilePath(_document);

            if (!string.IsNullOrEmpty(path))
            {
                string projectDir = Path.GetDirectoryName(path);
                if (Directory.Exists(projectDir))
                {
                    string saveFolder = Path.Combine(projectDir, "LayerToggles");
                    Directory.CreateDirectory(saveFolder);
                    return saveFolder;
                }
            }

            string projectName = _document.Title ?? "UnknownProject";
            string fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RK Tools", "CADManager", "LayerToggles", SanitizeFolderName(projectName));

            Directory.CreateDirectory(fallbackDir);
            return fallbackDir;
        }

        public bool IsUsingCustomProjectSaveFolder()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(_locationStore.Get(GetProjectKey()));
            }
            catch
            {
                // Settings must still open when the project location cannot be resolved.
                return false;
            }
        }

        public int GetSavedPresetCount()
        {
            string folder = GetProjectSaveFolder();
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Length
                : 0;
        }

        public int ConfigureProjectSaveFolder(string customFolder, bool moveExistingFiles)
        {
            string projectKey = GetProjectKey();
            string currentFolder = GetProjectSaveFolder();
            string destinationFolder = string.IsNullOrWhiteSpace(customFolder)
                ? GetDefaultProjectSaveFolder()
                : Path.GetFullPath(customFolder);

            Directory.CreateDirectory(destinationFolder);
            int movedFileCount = moveExistingFiles
                ? LayerToggleLocationStore.MovePresetFiles(currentFolder, destinationFolder)
                : 0;

            if (string.IsNullOrWhiteSpace(customFolder))
                _locationStore.Remove(projectKey);
            else
                _locationStore.Set(projectKey, destinationFolder);

            return movedFileCount;
        }

        private string GetProjectKey()
        {
            string path = TryGetActualFilePath(_document);
            if (string.IsNullOrWhiteSpace(path))
                path = _document.PathName;

            return !string.IsNullOrWhiteSpace(path)
                ? NormalizeProjectPath(path).ToUpperInvariant()
                : (_document.Title ?? "UnknownProject").Trim().ToUpperInvariant();
        }

        // Cloud models (BIM 360 / Autodesk Docs) report paths like "Autodesk Docs://Project/Model.rvt",
        // which Path.GetFullPath rejects. Use those paths verbatim as the key.
        private static string NormalizeProjectPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is PathTooLongException)
            {
                return path.Trim();
            }
        }

        private static PresetLayerState CreatePresetLayerState(
            LayerNode layerNode,
            IDictionary<string, object> savedLayers)
        {
            bool visibility = layerNode.IsChecked;
            string linePattern = layerNode.LinePattern;
            string lineColor = layerNode.LineColor;
            int? lineWeight = layerNode.LineWeight;

            string key = (layerNode.Name ?? string.Empty).Normalize(NormalizationForm.FormKC);
            if (savedLayers != null && savedLayers.TryGetValue(key, out object rawValue))
            {
                if (rawValue is bool legacyVisibility)
                {
                    visibility = legacyVisibility;
                }
                else if (rawValue != null)
                {
                    try
                    {
                        string layerJson = JsonConvert.SerializeObject(rawValue);
                        LayerData layerData = JsonConvert.DeserializeObject<LayerData>(layerJson);
                        if (layerData != null)
                        {
                            visibility = layerData.Visibility;
                            linePattern = layerData.LinePattern;
                            lineColor = layerData.LineColor;
                            lineWeight = layerData.LineWeight;
                        }
                    }
                    catch
                    {
                        // Preserve the current layer state when one saved entry is malformed.
                    }
                }
            }

            return new PresetLayerState(
                layerNode.Name,
                visibility,
                linePattern,
                lineColor,
                lineWeight);
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
                Notify($"Error searching for a matching template: {ex.Message}", true, false);
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
