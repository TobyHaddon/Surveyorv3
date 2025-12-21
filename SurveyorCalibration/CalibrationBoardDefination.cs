using Emgu.CV.Aruco;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor
{
    public partial class CalibrationBoardDefinition : INotifyPropertyChanged
    {
        // Event handler for property changed
        public event PropertyChangedEventHandler? PropertyChanged;

        // Board Types
        public enum TargetType
        {
            None = 0,
            ChArUco = 1,
            Chressboard = 2,
            SymmetricCircles = 3,
            AsymmetricCircles = 4
        }

        public CalibrationBoardDefinition()
        {
            Clear();
        }
        public void Clear()
        {
            this._target = TargetType.None;

            this._squaresX = 0;
            this._squaresY = 0;
            this._squareLength = 0f;
            this._markerLength = 0f;
            this._predefinedDictionaryName = PredefinedDictionaryName.Dict4X4_50;
            this._dictionary = null;
            this._board = null;

            _isDirty = false;
        }

        // Target Board Type
        private TargetType _target;
        public TargetType Target
        { 
            get => _target; 
            set
            {   if (_target != value)
                {
                    _target = value;
                    IsDirty = true;
                }
            }
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

        [JsonConverter(typeof(StringEnumConverter))]
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


        /// <summary>
        /// Use the board parameters to setup the dictionary and board 
        /// Ready for EMGU API calls
        /// </summary>
        /// <returns></returns>
        public bool Setup()
        {
            _dictionary = new Dictionary(PredefinedDictionaryName);

            if (_dictionary is not null)
            {
                _board = new CharucoBoard(SquaresX, SquaresY, SquareLength, MarkerLength, Dictionary);

                if (_board is not null)
                    return true;
            }

            return false;
        }


        /// <summary>
        /// Return a target board description string
        /// </summary>
        /// <returns></returns>
        public string Description()
        {
            // Board Caption
            string caption =
                $"ChArUco Target  {SquaresX} x {SquaresY} squares  " +
                $"{PredefinedDictionaryName}  Square:{(SquareLength * 1000):F2}mm " +
                $"Marker:{(MarkerLength * 1000):F2}mm";

            return caption;
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

