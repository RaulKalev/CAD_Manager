using CAD_Manager.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;

namespace CAD_Manager.Services
{
    /// <summary>
    /// Persists user-defined layer selection rules outside the Revit model.
    /// </summary>
    public sealed class LayerSelectionFilterStore
    {
        private readonly string _filePath;
        private readonly JsonSerializerSettings _serializerSettings;

        public LayerSelectionFilterStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CAD Manager",
                "layer-selection-filters.json"))
        {
        }

        public LayerSelectionFilterStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A filter settings path is required.", nameof(filePath));

            _filePath = filePath;
            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            _serializerSettings.Converters.Add(new StringEnumConverter());
        }

        public IReadOnlyList<LayerSelectionFilterRule> Load()
        {
            if (!File.Exists(_filePath))
                return new List<LayerSelectionFilterRule>();

            string json = File.ReadAllText(_filePath);
            List<LayerSelectionFilterRule> rules =
                JsonConvert.DeserializeObject<List<LayerSelectionFilterRule>>(json, _serializerSettings);
            return rules ?? new List<LayerSelectionFilterRule>();
        }

        public void Save(IEnumerable<LayerSelectionFilterRule> rules)
        {
            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonConvert.SerializeObject(
                rules ?? new List<LayerSelectionFilterRule>(),
                _serializerSettings);
            File.WriteAllText(_filePath, json);
        }
    }
}
