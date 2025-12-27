using Autodesk.Revit.DB;
using System;
using System.Reflection;

namespace CAD_Manager.Helpers
{
    public static class ElementIdExtensions
    {
        private static PropertyInfo _valueProperty;
        private static PropertyInfo _integerValueProperty;
        private static bool _initialized;

        private static void Initialize()
        {
            if (_initialized) return;

            var type = typeof(ElementId);
            _valueProperty = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            _integerValueProperty = type.GetProperty("IntegerValue", BindingFlags.Public | BindingFlags.Instance);
            _initialized = true;
        }

        public static long GetIdValue(this ElementId id)
        {
            if (id == null) return -1;
            
            Initialize();

            // Prefer "Value" (Revit 2024+ API, returns long)
            if (_valueProperty != null)
            {
                var val = _valueProperty.GetValue(id);
                if (val is long l) return l;
                if (val is int i) return (long)i;
            }

            // Fallback to "IntegerValue" (Revit <2024 API, returns int)
            if (_integerValueProperty != null)
            {
                var val = _integerValueProperty.GetValue(id);
                if (val is int i) return (long)i;
            }

            // If we get here, neither property exists or reflection failed.
            // DO NOT call id.IntegerValue directly to avoid MissingMethodException on new versions (JIT validation).
            return -1;
        }
    }
}
