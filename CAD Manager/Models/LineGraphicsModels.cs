using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Models
{
    public sealed class LineGraphicsTarget
    {
        public LineGraphicsTarget(ElementId importInstanceId, IEnumerable<string> layerNames)
        {
            ImportInstanceId = importInstanceId;
            LayerNames = (layerNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        public ElementId ImportInstanceId { get; }
        public IReadOnlyList<string> LayerNames { get; }
    }

    public sealed class LineGraphicsColor
    {
        public LineGraphicsColor(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
    }

    public sealed class LineGraphicsSnapshot
    {
        public LineGraphicsSnapshot(
            IEnumerable<string> patternNames,
            LineGraphicsColor color,
            string pattern,
            int? weight,
            bool colorVaries,
            bool patternVaries,
            bool weightVaries)
        {
            PatternNames = (patternNames ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
            Color = color;
            Pattern = pattern;
            Weight = weight;
            ColorVaries = colorVaries;
            PatternVaries = patternVaries;
            WeightVaries = weightVaries;
        }

        public IReadOnlyList<string> PatternNames { get; }
        public LineGraphicsColor Color { get; }
        public string Pattern { get; }
        public int? Weight { get; }
        public bool ColorVaries { get; }
        public bool PatternVaries { get; }
        public bool WeightVaries { get; }
    }
}
