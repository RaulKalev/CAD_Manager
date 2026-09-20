using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CAD_Manager.Core
{
    public enum LayerNameMatchType
    {
        Equals,
        Contains,
        StartsWith,
        EndsWith
    }

    /// <summary>
    /// A reusable rule for selecting layer rows by name.
    /// </summary>
    public sealed class LayerSelectionFilterRule : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private LayerNameMatchType _matchType = LayerNameMatchType.Contains;
        private string _pattern = string.Empty;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                    return;

                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }

        public LayerNameMatchType MatchType
        {
            get => _matchType;
            set
            {
                if (_matchType == value)
                    return;

                _matchType = value;
                OnPropertyChanged(nameof(MatchType));
                OnPropertyChanged(nameof(MatchTypeDisplayName));
            }
        }

        public string MatchTypeDisplayName
        {
            get
            {
                switch (MatchType)
                {
                    case LayerNameMatchType.StartsWith:
                        return "Starts with";
                    case LayerNameMatchType.EndsWith:
                        return "Ends with";
                    default:
                        return MatchType.ToString();
                }
            }
        }

        public string Pattern
        {
            get => _pattern;
            set
            {
                string nextValue = value ?? string.Empty;
                if (string.Equals(_pattern, nextValue, StringComparison.Ordinal))
                    return;

                _pattern = nextValue;
                OnPropertyChanged(nameof(Pattern));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class LayerSelectionFilterMatcher
    {
        /// <summary>
        /// Returns true when the layer name matches any enabled, non-empty rule.
        /// </summary>
        public static bool IsMatch(
            string layerName,
            IEnumerable<LayerSelectionFilterRule> rules)
        {
            if (layerName == null || rules == null)
                return false;

            foreach (LayerSelectionFilterRule rule in rules)
            {
                if (rule == null || !rule.IsEnabled || string.IsNullOrWhiteSpace(rule.Pattern))
                    continue;

                string pattern = rule.Pattern.Trim();
                switch (rule.MatchType)
                {
                    case LayerNameMatchType.Equals:
                        if (string.Equals(layerName, pattern, StringComparison.OrdinalIgnoreCase))
                            return true;
                        break;

                    case LayerNameMatchType.Contains:
                        if (layerName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                        break;

                    case LayerNameMatchType.StartsWith:
                        if (layerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                            return true;
                        break;

                    case LayerNameMatchType.EndsWith:
                        if (layerName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                            return true;
                        break;
                }
            }

            return false;
        }
    }
}
