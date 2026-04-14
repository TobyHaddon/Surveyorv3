// This code defines a UserControl named ReplicatesUserControl, which is part of the Surveyor application.
// The control is designed to display a layout of replicates, and optionally where they are in relation to the buoy line.
// The control dynamically builds its layout based on the provided ReplicatesLayoutClass data, and it includes checkboxes
// for selecting replicates when the control is enabled. The header images for the replicates change based on the current
// theme (light or dark). The control also raises a SelectionChanged event whenever a replicate's checkbox state changes.
// If the UserControl is disabled, the check boxes are hidden, and only the replicate names and their associated images
// are shown.
//
// Version 1.0  02 Apr 2026
//
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;


namespace Surveyor.User_Controls
{
    public sealed partial class ReplicatesUserControl : UserControl
    {
        public enum ReplicatesMode
        {
            Setup,
            View,
            Select
        }

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(ReplicatesMode),
                typeof(ReplicatesUserControl),
                new PropertyMetadata(ReplicatesMode.Select, OnModeChanged));

        public ReplicatesMode Mode
        {
            get => (ReplicatesMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        public event RoutedEventHandler? SelectionChanged;

        private FieldTrip? _fieldTrip = null;
        private HashSet<string>? _allowedReplicateNames = null;
        private readonly List<(CheckBox CheckBox, string ReplicateName)> _replicateCheckBoxes = [];
        private readonly List<(Image Image, bool IsBuoy)> _headerImages = [];

        /// <summary>
        /// Constructor for the ReplicatesUserControl. Initializes the component 
        /// and sets up event handlers for when the control's enabled state changes 
        /// and when the theme changes.
        /// </summary>
        public ReplicatesUserControl()
        {
            InitializeComponent();

            IsEnabledChanged += ReplicatesUserControl_IsEnabledChanged;
            ActualThemeChanged += ReplicatesUserControl_ActualThemeChanged;
        }

        /// <summary>
        /// Load the replicates layout into the control. This will dynamically build 
        /// the grid layout based on the provided data,
        /// </summary>
        /// <param name="layout"></param>
        public void SetFieldTrip(FieldTrip fieldTrip)
        {
            // Guard
            ArgumentNullException.ThrowIfNull(fieldTrip);

            _fieldTrip = fieldTrip;

            if (fieldTrip.IsAllReplicatesSameSetup())
                BuildLayout(null);
        }

        /// <summary>
        /// Only has an effect if the replicates layout has different setups for different sites. 
        /// If all replicates have the same setup then this method does nothing as the layout will 
        /// be the same for all sites. If there are different setups, then this method will update 
        /// the layout to match the provided site name or code.
        /// </summary>
        /// <param name="siteNameOrCode"></param>
        public void SetReplicateLayout(string siteNameOrCode)
        {
            // Guard
            if (_fieldTrip is null)
                return;

            if (!_fieldTrip.IsAllReplicatesSameSetup())
            {
                BuildLayout(siteNameOrCode);
            }
        }

        /// <summary>
        /// Used only in Mode="Select" to indicate which replicates this survey
        /// has been setup to analyze. The other replicates that are setup but 
        /// not in the allowed list will be grayed and unchecked.
        /// SetAllowedReplicates() must be called BEFORE SetSelected() which
        /// will indicate will of the allowed replicate are still available for
        /// selection
        /// </summary>
        /// <param name="selected"></param>
        public void SetAllowedReplicates(List<string> allowed)
        {
            // Guard
            if (Mode != ReplicatesMode.Select)
                // Invalid argument
                throw new ArgumentException("SetAllowedReplicates() should only be called if Mode=\"Select\".");


            // Set the check boxes for the selected replicates
            foreach ((CheckBox checkBox, string replicateName) in _replicateCheckBoxes)
            {
                if (allowed.Contains(replicateName))
                {
                    // Allowed
                    checkBox.IsEnabled = true;
                    checkBox.IsChecked = false;
                }
                else                
                {
                    // Not allowed
                    checkBox.IsChecked = false;
                    checkBox.IsEnabled = false;
                }
            }
        }




        /// <summary>
        /// Sets the selected replicates in the control based on a provided list of 
        /// replicate names.
        /// </summary>
        /// <param name="selected"></param>
        public void SetSelected(ObservableCollection<string> selected)
        {
            // Clear all check boxes first
            foreach ((CheckBox checkBox, _) in _replicateCheckBoxes)
                checkBox.IsChecked = false;

            // Set the check boxes for the selected replicates
            foreach ((CheckBox checkBox, string replicateName) in _replicateCheckBoxes)
            {
                if (selected.Contains(replicateName))
                {
                    if (Mode == ReplicatesMode.Setup)
                    {
                        checkBox.IsChecked = true;
                        checkBox.IsEnabled = true;
                    }
                    else if (Mode == ReplicatesMode.Select)
                    {
                        // Indicate transect number is already used and can't be changed
                        checkBox.IsChecked = null;
                        checkBox.IsEnabled = false;
                    }
                }
            }
        }


        /// <summary>
        /// Gets a List<> of the replicate names that are currently selected (checked)
        /// in the control.
        /// </summary>
        /// <returns></returns>
        public List<string> GetSelected()
        {
            return [.. _replicateCheckBoxes
                .Where(x => x.CheckBox.IsChecked == true)
                .Select(x => x.ReplicateName)];
        }


        ///
        /// EVENTS
        /// 

        /// <summary>
        /// Fired if the user set or unset a replicate checkbox. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReplicateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Mode == ReplicatesMode.Select && sender is CheckBox changedCheckBox && changedCheckBox.IsChecked == true)
            {
                EnforceSingleSelection(changedCheckBox);
            }

            SelectionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// This user control has been enabled or disabled. If disabled, the check boxes will 
        /// be hidden and only the replicate names and their associated images will be shown.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReplicatesUserControl_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateCheckBoxRowVisibility();
        }


        /// <summary>
        /// Theme has changed for the control. Update the header images to match the current theme (light or dark).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ReplicatesUserControl_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateHeaderImagesForTheme();
        }


        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ReplicatesUserControl control)
            {
                if (control.Mode == ReplicatesMode.Select)
                {
                    control.EnforceSingleSelection();
                }

                control.UpdateCheckBoxRowVisibility();
            }
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Dynamically builds a graphical representation of the transects required. 
        /// The grid layout for the replicates based on the provided ReplicatesLayoutClass data.
        /// </summary>

        private void BuildLayout(string? siteNameOrCode)
        {
            if (_fieldTrip is null) return;

            gridRoot.Children.Clear();
            gridRoot.ColumnDefinitions.Clear();
            _replicateCheckBoxes.Clear();
            _headerImages.Clear();

            // Default
            FieldTrip.DataClass.ReplicatesLayoutClass? replicatesLayout = null;

            // Check if there are different layouts for different sites, if so get the layout for the current site
            if (!_fieldTrip.IsAllReplicatesSameSetup() && siteNameOrCode is not null)
                replicatesLayout = _fieldTrip.GetReplicateLayout(siteNameOrCode);

            replicatesLayout ??= _fieldTrip.Data.ReplicatesLayout;

            bool topMarginRequired = replicatesLayout.Layout.Any(item => item.ReplicateItemType == FieldTrip.DataClass.ReplicatesItemType.BuoyLine);

            for (int column = 0; column < replicatesLayout.Layout.Count; column++)
            {
                FieldTrip.DataClass.ReplicatesItem item = replicatesLayout.Layout[column];

                // Guard
                if (item.ReplicateItemType is null)
                    continue;

                FieldTrip.DataClass.ReplicatesItemType itemType = (FieldTrip.DataClass.ReplicatesItemType)item.ReplicateItemType;

                bool isReplicate = itemType == FieldTrip.DataClass.ReplicatesItemType.Replicate;
                bool isBuoy = itemType == FieldTrip.DataClass.ReplicatesItemType.BuoyLine;
                bool isCompass = IsCompassType(itemType);

                double columnWidth = Resources["ReplicateColumnWidthValue"] is double w ? w : 32d;
                gridRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidth) });

                // Header row: Image for buoy/replicate, TextBlock for compass points
                if (isCompass)
                {
                    TextBlock compassText = new()
                    {
                        Text = GetCompassSymbol(itemType),
                        FontFamily = new FontFamily("Source Han Sans JP"),
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    Grid.SetRow(compassText, 0);
                    Grid.SetColumn(compassText, column);
                    gridRoot.Children.Add(compassText);
                }
                else
                {
                    // This is for the Buoy and Wave images.  If it is a Buoy image it need
                    // margin at the top.
                    Image image = new()
                    {
                        Margin = topMarginRequired ? new Thickness(0, 5, 0, 0) : new Thickness(0)
                    };

                    Grid.SetRow(image, 0);
                    Grid.SetColumn(image, column);
                    _headerImages.Add((image, isBuoy));
                    gridRoot.Children.Add(image);
                }

                // Arrow row only for replicate columns
                if (isReplicate)
                {
                    PathIcon arrow = new();

                    PathFigure figure = new()
                    {
                        StartPoint = new Point(0, 5),
                        IsClosed = true,
                        IsFilled = true
                    };

                    // Draw a horizontal double headed arrow. This is to represent a single transect line
                    figure.Segments.Add(new LineSegment { Point = new Point(5, 0) });
                    figure.Segments.Add(new LineSegment { Point = new Point(5, 3) });
                    figure.Segments.Add(new LineSegment { Point = new Point(25, 3) });
                    figure.Segments.Add(new LineSegment { Point = new Point(25, 0) });
                    figure.Segments.Add(new LineSegment { Point = new Point(30, 5) });
                    figure.Segments.Add(new LineSegment { Point = new Point(25, 10) });
                    figure.Segments.Add(new LineSegment { Point = new Point(25, 7) });
                    figure.Segments.Add(new LineSegment { Point = new Point(5, 7) });
                    figure.Segments.Add(new LineSegment { Point = new Point(5, 10) });

                    PathGeometry geometry = new();
                    geometry.Figures.Add(figure);
                    arrow.Data = geometry;

                    if (Resources["ArrowIconStyle"] is Style arrowStyle)
                    {
                        arrow.Style = arrowStyle;
                    }

                    Grid.SetRow(arrow, 1);
                    Grid.SetColumn(arrow, column);
                    gridRoot.Children.Add(arrow);
                }

                // Text label only on the replicate columns
                if (isReplicate)
                { 
                    TextBlock replicateText = new()
                    {
                        Text = item.ReplicateName ?? string.Empty
                    };

                    if (Resources["ReplicateNumberStyle"] is Style numberStyle)
                    {
                        replicateText.Style = numberStyle;
                    }

                    Grid.SetRow(replicateText, 2);
                    Grid.SetColumn(replicateText, column);
                    gridRoot.Children.Add(replicateText);
                }

                // Check boxes only for replicate columns
                if (isReplicate)
                {
                    bool isAllowed = _allowedReplicateNames == null
                        || _allowedReplicateNames.Count == 0
                        || _allowedReplicateNames.Contains(item.ReplicateName ?? string.Empty);

                    CheckBox checkBox = new();

                    if (Resources["ReplicateCheckBoxStyle"] is Style checkBoxStyle)
                    {
                        checkBox.Style = checkBoxStyle;
                    }

                    checkBox.Checked += ReplicateCheckBox_Changed;
                    checkBox.Unchecked += ReplicateCheckBox_Changed;

                    checkBox.IsEnabled = isAllowed;
                    checkBox.Visibility = isAllowed ? Visibility.Visible : Visibility.Collapsed;

                    Grid.SetRow(checkBox, 3);
                    Grid.SetColumn(checkBox, column);
                    gridRoot.Children.Add(checkBox);

                    _replicateCheckBoxes.Add((checkBox, item.ReplicateName ?? string.Empty));
                }
            }

            // Add an FontIcon "\uE783"; icon in a new column in row 3 with a tool tip to explain that
            // if the transect you require if grayed out then go to File>Settings then expand 'Survey Info & Media'
            // and tick the missing transect.
            if (Mode == ReplicatesMode.Select)
            {
                int infoColumn = replicatesLayout.Layout.Count;
                gridRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                FontIcon infoIcon = new()
                {
                    Glyph = "\uE783",
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                ToolTip toolTip = new()
                {
                    Content = "If the transect you require is grayed out, go to File>Settings, expand 'Survey Info & Media', and tick your missing transect."
                };
                ToolTipService.SetToolTip(infoIcon, toolTip);

                Grid.SetRow(infoIcon, 3);
                Grid.SetColumn(infoIcon, infoColumn);
                gridRoot.Children.Add(infoIcon);
            }

            UpdateHeaderImagesForTheme();
            UpdateCheckBoxRowVisibility();
        }

        private static bool IsCompassType(FieldTrip.DataClass.ReplicatesItemType itemType)
        {
            return itemType == FieldTrip.DataClass.ReplicatesItemType.North
                || itemType == FieldTrip.DataClass.ReplicatesItemType.South
                || itemType == FieldTrip.DataClass.ReplicatesItemType.East
                || itemType == FieldTrip.DataClass.ReplicatesItemType.West;
        }

        private static string GetCompassSymbol(FieldTrip.DataClass.ReplicatesItemType itemType)
        {
            return itemType switch
            {
                FieldTrip.DataClass.ReplicatesItemType.North => "\u24C3", // Ⓝ
                FieldTrip.DataClass.ReplicatesItemType.South => "\u24C8", // Ⓢ
                FieldTrip.DataClass.ReplicatesItemType.East => "\u24BA",  // Ⓔ
                FieldTrip.DataClass.ReplicatesItemType.West => "\u24CC",  // Ⓦ
                _ => string.Empty
            };
        }

        /// <summary>
        /// Hide/show the checkbox row
        /// </summary>
        private void UpdateCheckBoxRowVisibility()
        {
            bool showCheckBoxes = (Mode == ReplicatesMode.Select || Mode == ReplicatesMode.Setup);

            if (gridRoot.RowDefinitions.Count > 3)
            {
                gridRoot.RowDefinitions[3].Height = showCheckBoxes ? GridLength.Auto : new GridLength(0);
            }

            foreach ((CheckBox checkBox, _) in _replicateCheckBoxes)
            {
                checkBox.Visibility = showCheckBoxes ? Visibility.Visible : Visibility.Collapsed;

                if (Mode == ReplicatesMode.Select)
                    // In Mode="Select" the status is Checked if the user wants this transact name as the selected
                    // replicate. If the checkbox is set to indeterminate it means this replicate name has already
                    // been used in this survey
                    checkBox.IsThreeState = true;
                else
                    checkBox.IsThreeState = false;
            }
        }


        /// <summary>
        /// Update the header images to match the current theme (light or dark). 
        /// The buoy and waves images will change based on the theme.
        /// </summary>
        private void UpdateHeaderImagesForTheme()
        {
            bool lightTheme = ActualTheme == ElementTheme.Light;

            foreach ((Image image, bool isBuoy) in _headerImages)
            {
                string assetPath = isBuoy
                    ? (lightTheme ? "ms-appx:///Assets/buoy-Light.png" : "ms-appx:///Assets/buoy-Dark.png")
                    : (lightTheme ? "ms-appx:///Assets/waves-Light.png" : "ms-appx:///Assets/waves-Dark.png");

                image.Source = new BitmapImage(new Uri(assetPath));
            }
        }

        /// <summary>
        /// Used to ensure that only one checkbox is selected at a time. This is used in Select mode
        /// </summary>
        /// <param name="keepChecked"></param>
        private void EnforceSingleSelection(CheckBox? keepChecked = null)
        {
            bool oneKept = false;

            foreach ((CheckBox checkBox, _) in _replicateCheckBoxes)
            {
                if (checkBox.IsChecked == true)
                {
                    if (keepChecked != null)
                    {
                        if (!ReferenceEquals(checkBox, keepChecked))
                        {
                            checkBox.IsChecked = false;
                        }
                    }
                    else
                    {
                        if (!oneKept)
                        {
                            oneKept = true;
                        }
                        else
                        {
                            checkBox.IsChecked = false;
                        }
                    }
                }
            }
        }
    }
}

