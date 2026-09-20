using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Core
{
    public sealed class ViewDescriptor
    {
        public ViewDescriptor(long id, string name, string viewType)
        {
            Id = id;
            Name = name ?? string.Empty;
            ViewType = viewType ?? string.Empty;
        }

        public long Id { get; }
        public string Name { get; }
        public string ViewType { get; }
    }

    /// <summary>
    /// Revit-free selection and filtering state shared by the production
    /// Apply to Views window and the automated UI harness.
    /// </summary>
    public sealed class ViewSelectionState
    {
        private readonly IReadOnlyList<ViewDescriptor> _allViews;
        private readonly HashSet<long> _selectedIds = new HashSet<long>();

        public ViewSelectionState(IEnumerable<ViewDescriptor> views)
        {
            _allViews = (views ?? Enumerable.Empty<ViewDescriptor>())
                .Where(view => view != null)
                .GroupBy(view => view.Id)
                .Select(group => group.First())
                .OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<ViewDescriptor> AllViews => _allViews;

        public IReadOnlyCollection<long> SelectedIds => _selectedIds;

        public IReadOnlyList<ViewDescriptor> Filter(string query)
        {
            string normalized = query?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                return _allViews;

            return _allViews
                .Where(view => view.Name.IndexOf(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList()
                .AsReadOnly();
        }

        public void SetSelected(long id, bool isSelected)
        {
            if (!_allViews.Any(view => view.Id == id))
                return;

            if (isSelected)
                _selectedIds.Add(id);
            else
                _selectedIds.Remove(id);
        }

        public bool IsSelected(long id)
        {
            return _selectedIds.Contains(id);
        }

        public int CountSelectedVisible(IEnumerable<ViewDescriptor> visibleViews)
        {
            return (visibleViews ?? Enumerable.Empty<ViewDescriptor>())
                .Count(view => view != null && _selectedIds.Contains(view.Id));
        }

        public void ClearSelection()
        {
            _selectedIds.Clear();
        }
    }
}
