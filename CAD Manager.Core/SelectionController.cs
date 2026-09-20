using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Core
{
    /// <summary>
    /// Owns group/item selection semantics without depending on WPF or Revit.
    /// The view supplies the current canonical and filtered collections.
    /// </summary>
    public sealed class SelectionController<TGroup, TItem>
        where TGroup : class
        where TItem : class
    {
        private readonly Func<IEnumerable<TGroup>> _getAllGroups;
        private readonly Func<IEnumerable<TGroup>> _getVisibleGroups;
        private readonly Func<TGroup, IEnumerable<TItem>> _getItems;
        private readonly Func<TGroup, bool> _isGroupSelected;
        private readonly Action<TGroup, bool> _setGroupSelected;
        private readonly Func<TItem, bool> _isItemSelected;
        private readonly Action<TItem, bool> _setItemSelected;
        private readonly Func<TGroup, TGroup, bool> _groupsEqual;
        private readonly Func<TItem, TItem, bool> _itemsEqual;

        private TGroup _groupAnchor;
        private TItem _itemAnchor;

        public SelectionController(
            Func<IEnumerable<TGroup>> getAllGroups,
            Func<IEnumerable<TGroup>> getVisibleGroups,
            Func<TGroup, IEnumerable<TItem>> getItems,
            Func<TGroup, bool> isGroupSelected,
            Action<TGroup, bool> setGroupSelected,
            Func<TItem, bool> isItemSelected,
            Action<TItem, bool> setItemSelected,
            Func<TGroup, TGroup, bool> groupsEqual,
            Func<TItem, TItem, bool> itemsEqual)
        {
            _getAllGroups = getAllGroups ?? throw new ArgumentNullException(nameof(getAllGroups));
            _getVisibleGroups = getVisibleGroups ?? throw new ArgumentNullException(nameof(getVisibleGroups));
            _getItems = getItems ?? throw new ArgumentNullException(nameof(getItems));
            _isGroupSelected = isGroupSelected ?? throw new ArgumentNullException(nameof(isGroupSelected));
            _setGroupSelected = setGroupSelected ?? throw new ArgumentNullException(nameof(setGroupSelected));
            _isItemSelected = isItemSelected ?? throw new ArgumentNullException(nameof(isItemSelected));
            _setItemSelected = setItemSelected ?? throw new ArgumentNullException(nameof(setItemSelected));
            _groupsEqual = groupsEqual ?? throw new ArgumentNullException(nameof(groupsEqual));
            _itemsEqual = itemsEqual ?? throw new ArgumentNullException(nameof(itemsEqual));
        }

        public int SelectedCount
        {
            get
            {
                return AllGroups.Count(group => _isGroupSelected(group))
                    + AllGroups.Sum(group => Items(group).Count(item => _isItemSelected(item)));
            }
        }

        public void SelectGroup(TGroup group, bool controlPressed, bool shiftPressed)
        {
            if (group == null)
                return;

            if (shiftPressed && _groupAnchor != null)
                SelectGroupRange(_groupAnchor, group);
            else if (controlPressed)
                SetGroupSelection(group, !_isGroupSelected(group));
            else
            {
                ClearCore();
                SetGroupSelection(group, true);
            }

            _groupAnchor = group;
            _itemAnchor = null;
        }

        public void SelectItem(TItem item, bool controlPressed, bool shiftPressed)
        {
            if (item == null)
                return;

            if (shiftPressed && _itemAnchor != null)
                SelectItemRange(_itemAnchor, item);
            else if (controlPressed)
                _setItemSelected(item, !_isItemSelected(item));
            else
            {
                ClearCore();
                _setItemSelected(item, true);
            }

            _itemAnchor = item;
            _groupAnchor = null;
        }

        public void Clear()
        {
            ClearCore();
            _groupAnchor = null;
            _itemAnchor = null;
        }

        public bool SelectAnchoredGroupItems()
        {
            TGroup group = ResolveAnchorGroup();
            if (group == null)
                group = AllGroups.FirstOrDefault(candidate => _isGroupSelected(candidate));
            if (group == null)
                group = VisibleGroups.FirstOrDefault(candidate => _isGroupSelected(candidate));
            if (group == null)
                return false;

            foreach (TItem item in Items(group))
                _setItemSelected(item, true);

            return true;
        }

        public void SynchronizeAnchorWithVisibleItems()
        {
            if (_groupAnchor != null)
            {
                _groupAnchor = VisibleGroups.FirstOrDefault(group => _groupsEqual(group, _groupAnchor));
                return;
            }

            if (_itemAnchor != null && !VisibleGroups.SelectMany(Items).Any(item => _itemsEqual(item, _itemAnchor)))
                _itemAnchor = null;
        }

        private List<TGroup> AllGroups => (_getAllGroups() ?? Enumerable.Empty<TGroup>()).Where(group => group != null).ToList();

        private List<TGroup> VisibleGroups => (_getVisibleGroups() ?? Enumerable.Empty<TGroup>()).Where(group => group != null).ToList();

        private IEnumerable<TItem> Items(TGroup group)
        {
            return group == null
                ? Enumerable.Empty<TItem>()
                : (_getItems(group) ?? Enumerable.Empty<TItem>()).Where(item => item != null);
        }

        private void ClearCore()
        {
            foreach (TGroup group in AllGroups.Concat(VisibleGroups))
            {
                _setGroupSelected(group, false);
                foreach (TItem item in Items(group))
                    _setItemSelected(item, false);
            }
        }

        private void SetGroupSelection(TGroup group, bool isSelected)
        {
            foreach (TGroup candidate in AllGroups.Concat(VisibleGroups).Where(candidate => _groupsEqual(candidate, group)))
                _setGroupSelected(candidate, isSelected);
        }

        private void SelectGroupRange(TGroup start, TGroup end)
        {
            List<TGroup> groups = VisibleGroups;
            int startIndex = groups.FindIndex(group => _groupsEqual(group, start));
            int endIndex = groups.FindIndex(group => _groupsEqual(group, end));
            if (startIndex < 0 || endIndex < 0)
                return;

            int lower = Math.Min(startIndex, endIndex);
            int upper = Math.Max(startIndex, endIndex);
            for (int index = lower; index <= upper; index++)
                SetGroupSelection(groups[index], true);
        }

        private void SelectItemRange(TItem start, TItem end)
        {
            List<TItem> items = VisibleGroups.SelectMany(Items).ToList();
            int startIndex = items.FindIndex(item => _itemsEqual(item, start));
            int endIndex = items.FindIndex(item => _itemsEqual(item, end));
            if (startIndex < 0 || endIndex < 0)
                return;

            int lower = Math.Min(startIndex, endIndex);
            int upper = Math.Max(startIndex, endIndex);
            for (int index = lower; index <= upper; index++)
                _setItemSelected(items[index], true);
        }

        private TGroup ResolveAnchorGroup()
        {
            if (_groupAnchor != null)
                return AllGroups.FirstOrDefault(group => _groupsEqual(group, _groupAnchor)) ?? _groupAnchor;

            if (_itemAnchor == null)
                return null;

            return AllGroups.FirstOrDefault(group => Items(group).Any(item => _itemsEqual(item, _itemAnchor)))
                ?? VisibleGroups.FirstOrDefault(group => Items(group).Any(item => _itemsEqual(item, _itemAnchor)));
        }
    }
}
