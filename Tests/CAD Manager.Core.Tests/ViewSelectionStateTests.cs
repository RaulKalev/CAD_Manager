using CAD_Manager.Core;
using System.Linq;
using Xunit;

namespace CAD_Manager.Core.Tests
{
    public sealed class ViewSelectionStateTests
    {
        [Fact]
        public void Constructor_sorts_views_and_removes_duplicate_ids()
        {
            ViewSelectionState state = new ViewSelectionState(
                TestData.Views().Concat(new[] { new ViewDescriptor(10, "Duplicate", "Floor Plan") }));

            Assert.Equal(4, state.AllViews.Count);
            Assert.Equal("Coordination – This is a deliberately very long view name used for layout testing", state.AllViews[0].Name);
            Assert.Equal("Level 1", state.AllViews[1].Name);
        }

        [Fact]
        public void Filter_is_trimmed_and_case_insensitive()
        {
            ViewSelectionState state = new ViewSelectionState(TestData.Views());

            Assert.Equal(new long[] { 10, 20, 30 }, state.Filter("  LEVEL  ").Select(view => view.Id));
            Assert.Equal(4, state.Filter(string.Empty).Count);
        }

        [Fact]
        public void Selection_survives_filter_changes()
        {
            ViewSelectionState state = new ViewSelectionState(TestData.Views());
            state.SetSelected(10, true);
            state.SetSelected(30, true);

            Assert.Single(state.Filter("Level 1"));
            Assert.Equal(2, state.SelectedIds.Count);
            Assert.True(state.IsSelected(30));
            Assert.Equal(1, state.CountSelectedVisible(state.Filter("Level 1")));
        }

        [Fact]
        public void Unknown_ids_are_ignored_and_selection_can_be_cleared()
        {
            ViewSelectionState state = new ViewSelectionState(TestData.Views());
            state.SetSelected(999, true);
            state.SetSelected(20, true);

            Assert.Single(state.SelectedIds);
            state.ClearSelection();
            Assert.Empty(state.SelectedIds);
        }
    }
}
