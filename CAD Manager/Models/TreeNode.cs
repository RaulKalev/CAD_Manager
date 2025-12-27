using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CAD_Manager.Models
{
    public class TreeNode : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isHighlighted;
        private bool _isChecked;

        public string Header { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged(nameof(IsHighlighted));
                }
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));

                    // Update children
                    foreach (var child in Children)
                    {
                        child.IsChecked = value;
                    }
                }
            }
        }



        public ObservableCollection<TreeNode> Children { get; set; }

        public TreeNode(string header)
        {
            Header = header;
            Children = new ObservableCollection<TreeNode>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
