using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CAD_Manager.Models
{
    /// <summary>
    /// Represents a DWG node with visibility and layers.
    /// </summary>
    public class DWGNode : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        private bool _isChecked;
        private bool _isHalftone;
        private bool _isSelected;
        private bool _isVisibilityPending;
        private bool _isHalftonePending;
        private string _operationError;

        public string Name { get; set; }

        public bool IsChecked 
        { 
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        public bool IsHalftone
        {
            get => _isHalftone;
            set
            {
                if (_isHalftone != value)
                {
                    _isHalftone = value;
                    OnPropertyChanged(nameof(IsHalftone));
                }
            }
        }

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

        public bool IsVisibilityPending
        {
            get => _isVisibilityPending;
            set
            {
                if (_isVisibilityPending != value)
                {
                    _isVisibilityPending = value;
                    OnPropertyChanged(nameof(IsVisibilityPending));
                }
            }
        }

        public bool IsHalftonePending
        {
            get => _isHalftonePending;
            set
            {
                if (_isHalftonePending != value)
                {
                    _isHalftonePending = value;
                    OnPropertyChanged(nameof(IsHalftonePending));
                }
            }
        }

        public string OperationError
        {
            get => _operationError;
            set
            {
                if (_operationError != value)
                {
                    _operationError = value;
                    OnPropertyChanged(nameof(OperationError));
                }
            }
        }
        public bool IsExpanded 
        { 
            get => _isExpanded; 
            set 
            { 
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            } 
        }
        public List<LayerNode> Layers { get; set; }
        public ElementId ElementId { get; set; }
        
        // Graphic Overrides
        public string LinePattern { get; set; }
        public string LineColor { get; set; } // Hex string #RRGGBB
        public int? LineWeight { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a Layer node with visibility state.
    /// </summary>
    public class LayerNode : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isChecked;
        private bool _isSelected;
        private bool _isVisibilityPending;
        private string _operationError;

        public string Name { get; set; }
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

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

        public bool IsVisibilityPending
        {
            get => _isVisibilityPending;
            set
            {
                if (_isVisibilityPending != value)
                {
                    _isVisibilityPending = value;
                    OnPropertyChanged(nameof(IsVisibilityPending));
                }
            }
        }

        public string OperationError
        {
            get => _operationError;
            set
            {
                if (_operationError != value)
                {
                    _operationError = value;
                    OnPropertyChanged(nameof(OperationError));
                }
            }
        }

        public ElementId ElementId { get; set; }
        
        // Graphic Overrides
        public string LinePattern { get; set; }
        public string LineColor { get; set; } // Hex string #RRGGBB
        public int? LineWeight { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
