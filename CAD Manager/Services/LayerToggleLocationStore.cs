using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CAD_Manager.Services
{
    /// <summary>
    /// Stores an optional layer-toggle folder per Revit project.
    /// </summary>
    public sealed class LayerToggleLocationStore
    {
        private readonly string _settingsPath;

        public LayerToggleLocationStore(string settingsPath = null)
        {
            _settingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RK Tools",
                "CADManager",
                "layer-toggle-locations.json");
        }

        public string Get(string projectKey)
        {
            if (string.IsNullOrWhiteSpace(projectKey))
                return null;

            LayerToggleLocationSettings settings = Load();
            return settings.ProjectFolders.TryGetValue(projectKey, out string folder)
                ? folder
                : null;
        }

        public void Set(string projectKey, string folder)
        {
            if (string.IsNullOrWhiteSpace(projectKey))
                throw new ArgumentException("A project key is required.", nameof(projectKey));
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A folder is required.", nameof(folder));

            LayerToggleLocationSettings settings = Load();
            settings.ProjectFolders[projectKey] = Path.GetFullPath(folder);
            Save(settings);
        }

        public void Remove(string projectKey)
        {
            if (string.IsNullOrWhiteSpace(projectKey))
                return;

            LayerToggleLocationSettings settings = Load();
            if (settings.ProjectFolders.Remove(projectKey))
                Save(settings);
        }

        public static int MovePresetFiles(string sourceFolder, string destinationFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
                return 0;
            if (string.IsNullOrWhiteSpace(destinationFolder))
                throw new ArgumentException("A destination folder is required.", nameof(destinationFolder));

            string sourcePath = Path.GetFullPath(sourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destinationPath = Path.GetFullPath(destinationFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                return 0;

            Directory.CreateDirectory(destinationPath);
            string[] files = Directory.GetFiles(sourcePath, "*.json", SearchOption.TopDirectoryOnly);

            // Copy the complete set before deleting anything, so a copy failure leaves
            // the original project presets intact.
            foreach (string sourceFile in files)
            {
                string destinationFile = Path.Combine(destinationPath, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, true);
            }

            foreach (string sourceFile in files)
                File.Delete(sourceFile);

            return files.Length;
        }

        private LayerToggleLocationSettings Load()
        {
            if (!File.Exists(_settingsPath))
                return new LayerToggleLocationSettings();

            string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            LayerToggleLocationSettings settings =
                JsonConvert.DeserializeObject<LayerToggleLocationSettings>(json) ??
                new LayerToggleLocationSettings();
            settings.ProjectFolders = settings.ProjectFolders ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return settings;
        }

        private void Save(LayerToggleLocationSettings settings)
        {
            string directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json, Encoding.UTF8);
        }

        private sealed class LayerToggleLocationSettings
        {
            public Dictionary<string, string> ProjectFolders { get; set; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
