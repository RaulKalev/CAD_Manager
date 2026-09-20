using CAD_Manager.Core;
using System.Collections.Generic;

namespace CAD_Manager.Core.Tests
{
    internal static class TestData
    {
        public static IReadOnlyList<ViewDescriptor> Views()
        {
            return new List<ViewDescriptor>
            {
                new ViewDescriptor(30, "Level 3", "Floor Plan"),
                new ViewDescriptor(10, "Level 1", "Floor Plan"),
                new ViewDescriptor(20, "Level 2", "Floor Plan"),
                new ViewDescriptor(40, "Coordination – This is a deliberately very long view name used for layout testing", "Floor Plan")
            };
        }
    }
}
