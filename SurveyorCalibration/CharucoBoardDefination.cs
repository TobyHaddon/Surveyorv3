using Emgu.CV.Aruco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor
{
    public class CharucoBoardDefinition
    {
        private CharucoBoard? _board;
        public CharucoBoard? Board { get => _board; }

        public Dictionary? _dictionary;
        public Dictionary? Dictionary { get => _dictionary; }

        public int _squaresX;
        public int SquaresX { get => _squaresX; }

        public int _squaresY;
        public int SquaresY { get => _squaresY; }

        public float _squareLength;
        public float SquareLength { get => _squareLength; }

        public float _markerLength;
        public float MarkerLength { get => _markerLength; }

        public CharucoBoardDefinition()
        {
        }

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

        public void Clear()
        {
            this._dictionary = null;
            this._squaresX = 0;
            this._squaresY = 0;
            this._squareLength = 0f;
            this._markerLength = 0f;
            this._board = null;
        }
    }
}
