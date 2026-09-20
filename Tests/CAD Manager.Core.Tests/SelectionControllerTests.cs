using System.Collections.Generic;
using System.Linq;
using CAD_Manager.Core;
using Xunit;

namespace CAD_Manager.Core.Tests
{
    public sealed class SelectionControllerTests
    {
        [Fact]
        public void ShiftSelectionSelectsVisibleRangeAndSynchronizesEquivalentGroups()
        {
            Group first = Group.Create(1, "A");
            Group middle = Group.Create(2, "B");
            Group last = Group.Create(3, "C");
            List<Group> all = new List<Group> { first, middle, last };
            List<Group> visible = new List<Group>
            {
                Group.Create(1, "A"),
                Group.Create(2, "B"),
                Group.Create(3, "C")
            };
            SelectionController<Group, Item> controller = CreateController(all, () => visible);

            controller.SelectGroup(visible[0], false, false);
            controller.SelectGroup(visible[2], false, true);

            Assert.All(all, group => Assert.True(group.IsSelected));
            Assert.All(visible, group => Assert.True(group.IsSelected));
            Assert.Equal(3, controller.SelectedCount);
        }

        [Fact]
        public void FilteringAwayAnchorPreventsStaleRangeSelection()
        {
            Group first = Group.Create(1, "A");
            Group second = Group.Create(2, "B");
            List<Group> all = new List<Group> { first, second };
            List<Group> visible = new List<Group> { first, second };
            SelectionController<Group, Item> controller = CreateController(all, () => visible);

            controller.SelectItem(first.Items[0], false, false);
            visible = new List<Group> { second };
            controller.SynchronizeAnchorWithVisibleItems();
            controller.SelectItem(second.Items[0], false, true);

            Assert.False(first.Items[0].IsSelected);
            Assert.True(second.Items[0].IsSelected);
        }

        [Fact]
        public void SelectAnchoredGroupItemsSelectsOnlyThatGroupsItems()
        {
            Group first = Group.Create(1, "A", "A2");
            Group second = Group.Create(2, "B", "B2");
            List<Group> groups = new List<Group> { first, second };
            SelectionController<Group, Item> controller = CreateController(groups, () => groups);

            controller.SelectGroup(first, false, false);
            bool selected = controller.SelectAnchoredGroupItems();

            Assert.True(selected);
            Assert.All(first.Items, item => Assert.True(item.IsSelected));
            Assert.All(second.Items, item => Assert.False(item.IsSelected));
        }

        private static SelectionController<Group, Item> CreateController(
            List<Group> all,
            System.Func<List<Group>> getVisible)
        {
            return new SelectionController<Group, Item>(
                () => all,
                () => getVisible(),
                group => group.Items,
                group => group.IsSelected,
                (group, value) => group.IsSelected = value,
                item => item.IsSelected,
                (item, value) => item.IsSelected = value,
                (left, right) => left.Id == right.Id,
                (left, right) => ReferenceEquals(left, right));
        }

        private sealed class Group
        {
            public int Id { get; set; }
            public bool IsSelected { get; set; }
            public List<Item> Items { get; set; }

            public static Group Create(int id, params string[] itemNames)
            {
                string[] names = itemNames.Length == 0 ? new[] { "Item " + id } : itemNames;
                return new Group
                {
                    Id = id,
                    Items = names.Select(name => new Item { Name = name }).ToList()
                };
            }
        }

        private sealed class Item
        {
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }
    }
}
