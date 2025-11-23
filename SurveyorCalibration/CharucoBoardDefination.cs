using Emgu.CV.Aruco;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor
{
    public partial class CharucoBoardDefinition : INotifyPropertyChanged
    {
        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        public CharucoBoardDefinition()
        {
            Clear();
        }
        public void Clear()
        {
            this._dictionary = null;
            this._squaresX = 0;
            this._squaresY = 0;
            this._squareLength = 0f;
            this._markerLength = 0f;
            this._board = null;

            _isDirty = false;
        }

        // Number of squares in X direction
        private int _squaresX;
        public int SquaresX 
        { 
            get => _squaresX; 
            set
            {   if (_squaresX != value)
                {
                    _squaresX = value;
                    IsDirty = true;
                }
            }            
        }

        // Number of squares in Y direction
        private int _squaresY;
        public int SquaresY 
        { 
            get => _squaresY; 
            set
            {   if (_squaresY != value)
                {
                    _squaresY = value;
                    IsDirty = true;
                }
            }            
        }

        // Square length in m
        private float _squareLength;
        public float SquareLength 
        { 
            get => _squareLength; 
            set
            {   if (_squareLength != value)
                {
                    _squareLength = value;
                    IsDirty = true;
                }
            }            
        }

        // Marker length in m
        private float _markerLength;
        public float MarkerLength 
        { 
            get => _markerLength; 
            set
            {
                if (_markerLength != value)
                {
                    _markerLength = value;
                    IsDirty = true;
                }
            }

        }

        // Dictionary Name
        private PredefinedDictionaryName _predefinedDictionaryName;

        public PredefinedDictionaryName PredefinedDictionaryName
        {
            get => _predefinedDictionaryName;
            set
            {
                if (_predefinedDictionaryName != value)
                {
                    _predefinedDictionaryName = value;
                    IsDirty = true;
                }
            }
        }

        [JsonIgnore]
        private bool _isDirty;

        [JsonIgnore]
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        private CharucoBoard? _board;

        [JsonIgnore]
        public CharucoBoard? Board { get => _board; }

        private Dictionary? _dictionary;

        [JsonIgnore]
        public Dictionary? Dictionary { get => _dictionary; }


        public void Setup(Dictionary dictionary, int SquaresX, int SquaresY, float SquareLength, float MarkerLength)
        {
            this._dictionary = dictionary;
            this._squaresX = SquaresX;
            this._squaresY = SquaresY;
            this._squareLength = SquareLength;
            this._markerLength = MarkerLength;

            // Create the CharucoBoard if it is not already created
            if (_board is null && Dictionary is not null)
            {
                _board = new CharucoBoard(SquaresX, SquaresY, SquareLength, MarkerLength, Dictionary);
            }
        }


        ///
        /// EVENTS
        /// 
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

