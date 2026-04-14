// This code defines a user control named `FieldTripInfoUserControl`.
// The control is likely designed to display information about field trips.
// 
// Version 1.0  04 Apr 2026
//
using MathNet.Numerics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Networking.Connectivity;
using static Surveyor.FieldTrip;

namespace Surveyor.User_Controls
{
    public sealed partial class FieldTripInfoUserControl : UserControl
    {
        // Hidden key
        private readonly List<string> cacheFileList = [@"cache\B2OieFqWFQshZLyoD04JT.html", @"cache\diPaZmA8SBct5nUFzlIXF.html", @"cache\n2gxpBTmt9JQQJ99CDACi.html", @"cache\5Ypz1szlaAAAgAZMP24aR.html"];

        private FieldTrip? _fieldTrip = null;

        private readonly ObservableCollection<SurveyReplicateCardItem> _surveyReplicateCards = [];

        private Point _lastMapPointerPointInMap;
        //???private bool _hasLastMapPointerPoint;

        private readonly TextBlock _mapIconPopupText = new()
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,           
        };

        private readonly Popup _mapIconPopup = new()
        {
            IsLightDismissEnabled = true
        };


        private readonly Brush activeBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
                        ?? new SolidColorBrush(Colors.Gray);

        private readonly Brush inactiveBrush = Application.Current.Resources["ControlStrokeColorSecondaryBrush"] as Brush
                                ?? new SolidColorBrush(Colors.Gray)/* { Opacity = 0.45 }*/;
        
        private const double InitialZoomLevel = 15d;
        private bool _pendingInitialMapView;

        public FieldTripInfoUserControl()
        {
            InitializeComponent();
            SurveyReplicatesGridView.ItemsSource = _surveyReplicateCards;

            // In constructor (after InitializeComponent / ItemsSource)
            _mapIconPopup.Child = new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                Background = new SolidColorBrush(Colors.Black),
                Child = _mapIconPopupText
            };

            // Make key
            FieldTripMapControl.MapServiceToken = string.Join("",
                            cacheFileList.Select(Path.GetFileNameWithoutExtension));


            // Ensure XamlRoot is available once control is loaded
            FieldTripMapControl.SizeChanged += FieldTripMapControl_SizeChanged;
        }

        /// <summary>
        /// Load the replicates layout into the control. This will dynamically build 
        /// the grid layout based on the provided data,
        /// </summary>
        /// <param name="layout"></param>
        public void SetFieldTrip(FieldTrip fieldTrip)
        {
            _fieldTrip = fieldTrip;

            LoadControls();
        }


        /// <summary>
        /// Accessed from XAML and returns the total number of sites
        /// </summary>
        public string NumberOfSites
        {
            get
            {
                if (_fieldTrip is null)
                    return "0 reefs";
                int count = _fieldTrip.Data.Surveys.SurveyItemList.Count(s => s.Active);
                return $"{count} reef{(count == 1 ? "" : "s")}";            }
        }


        /// <summary>
        /// Accessed from XAML and calculates the total number of surveys per seasons
        /// </summary>
        public string TotalSurveys
        {
            get
            {
                if (_fieldTrip is null)
                    return "0";

                int totalSurveys = 0;
                foreach (DataClass.SurveyItem item in _fieldTrip.Data.Surveys.SurveyItemList)
                {
                    if (item.Active)
                    {
                        string? siteNameOrCode = !string.IsNullOrEmpty(item.SiteCode) ? item.SiteCode : item.SiteName;

                        if (siteNameOrCode is not null)
                        {
                            FieldTrip.DataClass.ReplicatesLayoutClass? layout = _fieldTrip.GetReplicateLayout(siteNameOrCode);
                            if (layout is not null)
                            {
                                int replicateCount = layout?.Layout.Count(x => x.ReplicateItemType == DataClass.ReplicatesItemType.Replicate) ?? 0;

                                totalSurveys += (item.Depths.Count * replicateCount);
                            }
                        }
                    }
                }

                return totalSurveys.ToString();
            }
        }


        /// <summary>
        /// Accessed from XAML, returns the transect length in meters if setup 
        /// </summary>
        public string TransectLength
        {
            get
            {
                if (_fieldTrip is null)
                    return "";

                if (_fieldTrip.Data.SurveyRules.TransectLength != 0)
                    return $"{_fieldTrip.Data.SurveyRules.TransectLength}m";
                else
                    return $"Not setup!";
            }
        }

        /// <summary>
        /// Accessed from XAML, returns the species list for this field trip
        /// </summary>
        public string SpeciesListName
        {
            get 
            {
                if (_fieldTrip is null)
                    return "";

                return _fieldTrip.Data.Info.SpeciesListName ?? "";
            }
        }


        /// <summary>
        /// Accessed from XAML, returns the species list for this field trip
        /// </summary>
        public string FieldTripTemplateName
        {
            get
            {
                if (_fieldTrip is null)
                    return "";

                return _fieldTrip.Data.Info.FieldTripFileName ?? "";
            }
        }



        ///
        /// EVENTS
        ///




        /// <summary>
        /// Sets the initial zoom level for the map
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void FieldTripMapControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TryApplyInitialMapView();
        }

        private void TryApplyInitialMapView()
        {
            if (!_pendingInitialMapView)
                return;

            if (FieldTripMapControl.ActualWidth <= 0 || FieldTripMapControl.ActualHeight <= 0)
                return;

            FieldTripMapControl.ZoomLevel = InitialZoomLevel;
            _pendingInitialMapView = false;
        }


        /// <summary>
        /// Display the popup when user clicks on a map icon, and also capture the pointer position for fine-grained popup placement.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //??? to be deleted - MapControl actually never fires PointerPressed
        //private void FieldTripMapControl_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        //{
        //    _lastMapPointerPointInMap = e.GetCurrentPoint(FieldTripMapControl).Position;
        //    //???_hasLastMapPointerPoint = true;
        //    _mapIconPopup.IsOpen = false;
        //}


        /// <summary>
        /// Load each replicate user control
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyReplicatesUserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_fieldTrip is null || sender is not ReplicatesUserControl replicatesControl)
                return;

            if (replicatesControl.Tag is string siteCode && !string.IsNullOrWhiteSpace(siteCode))
            {
                replicatesControl.Mode = ReplicatesUserControl.ReplicatesMode.View;
                replicatesControl.SetFieldTrip(_fieldTrip);
                replicatesControl.SetReplicateLayout(siteCode);
            }
        }


        /// <summary>
        /// Download the reef map
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DownloadMapButton_Click(object sender, RoutedEventArgs e)
        {
            // User explicitly opted in on metered network.
            FieldTripMapBorder.Visibility = Visibility.Visible;
            DownloadMapButton.Visibility = Visibility.Collapsed;

            LoadMap();
        }


        /// <summary>
        /// User click on one of the map icons (map pins)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void FieldTripMapControl_MapElementClick(MapControl sender, MapElementClickEventArgs args)
        {
            if (args.Element is MapIcon mapIcon)
            {
                BasicGeoposition clicked = mapIcon.Location.Position;
                SurveyReplicateCardItem? matchedItem = null;
                
                foreach (SurveyReplicateCardItem item in _surveyReplicateCards)
                {
                    bool isMatch = item.Latitude.HasValue
                        && item.Longitude.HasValue
                        && NearlyEqual(item.Latitude.Value, clicked.Latitude)
                        && NearlyEqual(item.Longitude.Value, clicked.Longitude);

                    if (isMatch)
                    {
                        Debug.WriteLine($"Found transect {item.TitleText} Lat:{item.Latitude:F5}, Long:{item.Longitude:F5}");
                        matchedItem = item;
                    }

                    item.CardBorderBrush = isMatch
                        ? new SolidColorBrush(Colors.Orange)
                        : (item.IsActive ? activeBrush : inactiveBrush);
                }

                // Prepare the popup text
                string title = matchedItem?.TitleText?.TrimEnd(':') ?? "Survey site";
                _mapIconPopupText.Text = $"{title}\nLat: {clicked.Latitude:F5}\nLon: {clicked.Longitude:F5}";

                // Apply the XamlRoot to the popup if not already done so
                if (_mapIconPopup.XamlRoot is null || !ReferenceEquals(_mapIconPopup.XamlRoot, sender.XamlRoot))
                {
                    _mapIconPopup.XamlRoot = sender.XamlRoot ?? XamlRoot;
                }

                // Click position is map-relative
                Point mapPoint = GetMousePositionRelativeTo(FieldTripMapControl);

                // Convert map-relative point to window/root coordinates for Popup offsets
                if (XamlRoot?.Content is UIElement rootElement)
                {
                    GeneralTransform transform = sender.TransformToVisual(rootElement);
                    Point anchorInRoot = transform.TransformPoint(mapPoint);

                    _mapIconPopup.HorizontalOffset = Math.Max(0, anchorInRoot.X + 12);
                    _mapIconPopup.VerticalOffset = Math.Max(0, anchorInRoot.Y - 70);
                    _mapIconPopup.IsOpen = true;
                }
            }

            static bool NearlyEqual(double a, double b, double epsilon = 1e-6)
            {
                return Math.Abs(a - b) <= epsilon;
            }
        }


        ///
        /// PRIVATE
        /// 

        private void LoadControls()
        {
            if (_fieldTrip is null)
                return;

            ApplyMapVisibility();

            if (FieldTripMapBorder.Visibility == Visibility.Visible)
            {
                LoadMap();
            }

            string areaName = _fieldTrip.Data.Info.AreaName ?? string.Empty;
            string areaCode = _fieldTrip.Data.Info.AreaCode ?? string.Empty;
            string countryName = _fieldTrip.Data.Info.CountryName ?? string.Empty;
            string countryCode = _fieldTrip.Data.Info.CountryCode ?? string.Empty;

            // Make the title
            string title = string.Empty;
            if (string.Compare(areaName, areaCode, true) != 0)
            {
                title = string.IsNullOrWhiteSpace(areaName) && string.IsNullOrWhiteSpace(areaCode)
                                ? "Field Trip"
                                : $"{areaName} ({areaCode})";
            }
            else
            {
                // Area Code and Area Name are same so no need to display both
                title = string.IsNullOrWhiteSpace(areaName)
                                ? "Field Trip"
                                : $"{areaName}";
            }
            string countryText = string.Empty;
            if (!string.IsNullOrEmpty(countryName))
                countryText += $" {countryName}";
            if (!string.IsNullOrEmpty(countryCode))
                countryText += $"({countryCode})";
            if (!string.IsNullOrEmpty(countryText))
                title += $" {countryText}";

            FieldTripTitleText.Text = title;

            // Load the replicates display
            LoadSurveyReplicateCards(activeOnly: false);

        }


        /// <summary>
        /// Load the replicates display
        /// </summary>
        /// <param name="activeOnly"></param>
        private void LoadSurveyReplicateCards(bool activeOnly)
        {
            _surveyReplicateCards.Clear();

            if (_fieldTrip is null)
                return;
           
            // Load the active replicates
            foreach (FieldTrip.DataClass.SurveyItem surveyItem in _fieldTrip.Data.Surveys.SurveyItemList)
            {
                if (surveyItem.Active)
                {
                    string siteCode = surveyItem.SiteCode ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(siteCode))
                        continue;

                    string title = _fieldTrip.GetTitleSummary(siteCode, TitleSummaryLineType.SiteAndDepth);
                    if (!title.EndsWith(':'))
                        title += ":";

                    _surveyReplicateCards.Add(new SurveyReplicateCardItem
                    {
                        SiteCode = siteCode,
                        TitleText = title,
                        IsActive = surveyItem.Active,
                        Latitude = surveyItem.CoordinatesLatitude,
                        Longitude = surveyItem.CoordinatesLongitude,
                        CardBorderBrush = activeBrush,
                    });
                }
            }

            // Load the inactive replicates (if required)
            if (!activeOnly)
            {
                foreach (FieldTrip.DataClass.SurveyItem surveyItem in _fieldTrip.Data.Surveys.SurveyItemList)
                {
                    if (!surveyItem.Active)
                    {
                        string siteCode = surveyItem.SiteCode ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(siteCode))
                            continue;

                        string title = _fieldTrip.GetTitleSummary(siteCode, TitleSummaryLineType.SiteAndDepth);
                        if (!title.EndsWith(":"))
                            title += ":";

                        _surveyReplicateCards.Add(new SurveyReplicateCardItem
                        {
                            SiteCode = siteCode,
                            TitleText = title,
                            CardBorderBrush = inactiveBrush
                        });
                    }
                }
            }
        }



        /// <summary>
        /// Get the internet availability
        /// </summary>
        /// <returns></returns>
        private enum MapNetworkState
        {
            None,
            Metered,
            Unmetered
        }
        private static MapNetworkState GetMapNetworkState()
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
                return MapNetworkState.None;

            NetworkConnectivityLevel level = profile.GetNetworkConnectivityLevel();
            bool hasInternet = level == NetworkConnectivityLevel.InternetAccess
                || level == NetworkConnectivityLevel.ConstrainedInternetAccess;

            if (!hasInternet)
                return MapNetworkState.None;

            ConnectionCost? cost = profile.GetConnectionCost();
            bool isMetered = cost is not null
                && (cost.NetworkCostType == NetworkCostType.Fixed
                    || cost.NetworkCostType == NetworkCostType.Variable
                    || cost.Roaming
                    || cost.OverDataLimit
                    || cost.ApproachingDataLimit);

            return isMetered ? MapNetworkState.Metered : MapNetworkState.Unmetered;
        }

        private void ApplyMapVisibility()
        {
            MapNetworkState state = GetMapNetworkState();

            FieldTripMapBorder.Visibility = Visibility.Collapsed;
            DownloadMapButton.Visibility = Visibility.Collapsed;

            switch (state)
            {
                case MapNetworkState.Unmetered:
                    FieldTripMapBorder.Visibility = Visibility.Visible;
                    break;

                case MapNetworkState.Metered:
                    DownloadMapButton.Visibility = Visibility.Visible;
                    break;

                case MapNetworkState.None:
                default:
                    // keep both collapsed
                    break;
            }
        }


        /// <summary>
        /// Load the reef map
        /// </summary>      
        private void LoadMap()
        {
            if (_fieldTrip is null)
                return;

            FieldTripMapControl.Layers.Clear();


            FieldTripMapControl.MapServiceErrorOccurred += (sender, args) =>
            {
                // Log or display the error
                System.Diagnostics.Debug.WriteLine("Map service error occurred.");
            };

            var surveySites = _fieldTrip.Data.Surveys.SurveyItemList
                .Where(s => s.Active == true && s.CoordinatesLatitude.HasValue && s.CoordinatesLongitude.HasValue)
                .ToList();

            if (surveySites.Count == 0)
                return;

            try
            {
                MapElementsLayer layer = new();

                foreach (var site in surveySites)
                {
                    Geopoint location = new(new BasicGeoposition
                    {
                        Latitude = site.CoordinatesLatitude!.Value,
                        Longitude = site.CoordinatesLongitude!.Value
                    });

                    MapIcon icon = new()
                    {
                        Location = location
                    };
                    layer.MapElements.Add(icon);
                }

                FieldTripMapControl.Layers.Add(layer);

                double centerLat = surveySites.Average(s => s.CoordinatesLatitude!.Value);
                double centerLon = surveySites.Average(s => s.CoordinatesLongitude!.Value);

                FieldTripMapControl.Center = new Geopoint(new BasicGeoposition
                {
                    Latitude = centerLat,
                    Longitude = centerLon,
                });

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading map: {ex.Message}");
            }
        }
        

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        public static Point GetMousePositionRelativeTo(UIElement target)
        {
            if (target.XamlRoot is null)
                throw new InvalidOperationException("Target must be attached to a XAML tree.");

            if (!GetCursorPos(out POINT screenPt))
                throw new InvalidOperationException("GetCursorPos failed.");

            // Screen -> XamlRoot local coordinates
            Point rootPoint = target.XamlRoot.CoordinateConverter
                .ConvertScreenToLocal(new PointInt32(screenPt.X, screenPt.Y));

            // XamlRoot root element -> target element coordinates
            UIElement rootElement = (UIElement)target.XamlRoot.Content;
            GeneralTransform transform = rootElement.TransformToVisual(target);

            return transform.TransformPoint(rootPoint);
        }
    }


    public sealed class SurveyReplicateCardItem : INotifyPropertyChanged
    {
        private Brush _cardBorderBrush = new SolidColorBrush(Colors.Gray);

        public string SiteCode { get; set; } = string.Empty;
        public string TitleText { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public Brush CardBorderBrush
        {
            get => _cardBorderBrush;
            set
            {
                if (!ReferenceEquals(_cardBorderBrush, value))
                {
                    _cardBorderBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
