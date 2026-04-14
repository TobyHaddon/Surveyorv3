using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using static Surveyor.User_Controls.SurveyInfoUserControl;

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyCodeUserControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public enum SurveyCodeMode
        {
            Setup,
            LimitedEdit,
            View
        }

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(SurveyCodeMode),
                typeof(SurveyCodeUserControl),
                new PropertyMetadata(SurveyCodeMode.Setup, OnModeChanged));

        public SurveyCodeMode Mode
        {
            get => (SurveyCodeMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SurveyCodeUserControl control)
            {
                control.ApplyMode();
            }
        }


        public enum SurveyCodeStructure
        {
            Structured,
            Unstructured
        }

        public static readonly DependencyProperty StructureProperty =
            DependencyProperty.Register(
                nameof(Structure),
                typeof(SurveyCodeStructure),
                typeof(SurveyCodeUserControl),
                new PropertyMetadata(SurveyCodeStructure.Structured, OnStructureChanged));

        public SurveyCodeStructure Structure
        {
            get => (SurveyCodeStructure)GetValue(StructureProperty);
            set => SetValue(StructureProperty, value);
        }
      
        private static void OnStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SurveyCodeUserControl control)
            {
                control.ApplyMode();
            }
        }

        private FieldTrip? _fieldTrip = null;

        public event RoutedEventHandler? SelectionChanged;

        private readonly string _siteSelectorOriginalText;
        private readonly string _depthSelectorOriginalText;


        private Visibility _structuredVisibility = Visibility.Collapsed;
        public Visibility StructuredVisibility
        {
            get => _structuredVisibility;
            set
            {
                if (_structuredVisibility != value)
                {
                    _structuredVisibility = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StructuredVisibility)));
                }
            }
        }

        private Visibility _structuredVisibilityReplicates = Visibility.Collapsed;  // The replicates control only appears if Structure=Structured and a Site Name has been selected
        public Visibility StructuredVisibilityReplicates
        {
            get => _structuredVisibilityReplicates;
            set
            {
                if (_structuredVisibilityReplicates != value)
                {
                    _structuredVisibilityReplicates = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StructuredVisibilityReplicates)));
                }
            }
        }

        private Visibility _unstructuredVisibility = Visibility.Collapsed;
        public Visibility UnstructuredVisibility
        {
            get => _unstructuredVisibility;
            set
            {
                if (_unstructuredVisibility != value)
                {
                    _unstructuredVisibility = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnstructuredVisibility)));
                }
            }
        }


        public SurveyCodeUserControl()
        {
            InitializeComponent();

            ReplicatesUserControl.SelectionChanged += ReplicatesUserControl_SelectionChanged;

            ApplyMode();

            // Get the initial text from the Site & Depth selectors. These are to prompt the user
            // as to what the UI control is for. We remember these value so we do not confuse them with 
            // selected values
            _siteSelectorOriginalText = (string)SiteDropDown.Content;
            _depthSelectorOriginalText = (string)DepthDropDown.Content;

            ResetFieldTrip();

            // Start data blank
            SurveyDatePicker.Date = DateTime.Now;

            // Hide the Replicate user control and let the setting of a site make it visible
            StructuredVisibilityReplicates = Visibility.Collapsed;
        }


        /// <summary>
        /// Call if the application is under the control of a field trip template. This limits the user 
        /// to only select from the sites, depths and replicates layouts defined in the template.
        /// </summary>
        /// <param name="layout"></param>
        public void SetFieldTrip(FieldTrip fieldTrip)
        {
            _fieldTrip = fieldTrip;

            ResetDialogFields();

            // Set Structure type
            Structure = SurveyCodeStructure.Structured;

            // Hide unstructured fields
            UnstructuredVisibility = Visibility.Collapsed;

            // Show structured fields
            StructuredVisibility = Visibility.Visible;

            if (Structure == SurveyCodeStructure.Structured && _fieldTrip?.IsAllReplicatesSameSetup() == true && !string.IsNullOrWhiteSpace(GetSiteName()))
            {
                StructuredVisibilityReplicates = Visibility.Visible;                
            }
            else
            {
                StructuredVisibilityReplicates = Visibility.Collapsed;                
            }

            ReplicatesUserControl.SetFieldTrip(fieldTrip);
            LoadSites();           
        }


        /// <summary>
        /// Reset and field trip (i.e. no field trip attached)
        /// </summary>
        public void ResetFieldTrip()
        {   
            _fieldTrip = null;

            // Set Structure type
            Structure = SurveyCodeStructure.Unstructured;

            // Hide structured fields
            StructuredVisibility = Visibility.Collapsed;
            StructuredVisibilityReplicates = Visibility.Collapsed;
            
            // Show unstructured fields
            UnstructuredVisibility = Visibility.Visible;
          
            ResetDialogFields();
        }


        /// <summary>
        /// Clear all the dialog fields
        /// </summary>
        public void ResetDialogFields()
        {
            // Reset structured fields
            SiteDropDown.Content = _siteSelectorOriginalText;
            DepthDropDown.Content = _depthSelectorOriginalText;
            SurveyDatePicker.Date = DateTime.Now;
            ReplicatesUserControl.SetSelected([]);

            // Reset unstructured fields
            LoadDefaultDepthList();
            SurveyCode.Text = "";
            SurveyDepth.Text = string.Empty;
            SurveyDepth.SelectedItem = null;  // Clear the selected item
            SurveyDepth.SelectedIndex = -1;  // Clear the selected index            
        }


        /// <summary>
        /// Set the survey site name field either by passing the site name
        /// or the site code
        /// </summary>
        /// <param name="siteNameOrCode"></param>
        public void SetSiteNameOrCode(string siteNameOrCode)
        {
            if (!string.IsNullOrEmpty(siteNameOrCode))
            {
                // Check if maybe a site code
                string? siteName = null;
                if (_fieldTrip is not null)
                    siteName = _fieldTrip.GetSiteNameFromCode(siteNameOrCode);

                if (string.IsNullOrEmpty(siteName))
                    // Assume this is a site name
                    SetSite(siteNameOrCode);
                else
                    SetSite(siteName);
            }
            else
                SetSite(null);
        }


        /// <summary>
        /// Get the survey site name
        /// </summary>
        /// <returns></returns>
        public string GetSiteName()
        {
            if (Structure == SurveyCodeStructure.Structured)
            {
                if ((string)SiteDropDown.Content != _siteSelectorOriginalText)
                    return (string)SiteDropDown.Content;
                else
                    return string.Empty;
            }
            else
                return string.Empty;
        }


        /// <summary>
        /// Get the survey site code
        /// </summary>
        /// <returns></returns>
        public string GetSiteCode()
        {
            string? siteCode = null;

            if (Structure == SurveyCodeStructure.Structured)
            {
                string siteName = GetSiteName();

                if (_fieldTrip is not null && !string.IsNullOrEmpty(siteName))
                    siteCode = _fieldTrip.GetSiteCodeFromName(siteName);
            }

            return siteCode is not null ? siteCode : "";
        }


        /// <summary>
        /// Set the survey depth
        /// </summary>
        /// <param name="depth"></param>
        public void SetDepth(string depth)
        {            
            DepthDropDown.Content = depth;
            StructuredMakeSurveyCodeAndUpdate();
        }


        /// <summary>
        /// Get the survey depth
        /// </summary>
        /// <returns></returns>
        public string GetDepth()
        {
            if (Structure == SurveyCodeStructure.Structured)
            {
                if ((string)SiteDropDown.Content != _depthSelectorOriginalText)
                    return (string)DepthDropDown.Content;
                else
                    return string.Empty;
            }
            else
                return SurveyDepth.Text;
        }


        /// <summary>
        /// Set the survey date time
        /// </summary>
        /// <param name="surveyDate"></param>
        public void SetSurveyDate(DateTime surveyDate)
        {
            SurveyDatePicker.Date = surveyDate;
            StructuredMakeSurveyCodeAndUpdate();
        }


        /// <summary>
        /// Get the survey date time
        /// </summary>
        /// <returns></returns>
        public DateTime GetSurveyDate()
        {
            if (Structure == SurveyCodeStructure.Structured)
                return SurveyDatePicker.Date.Date;
            else
                return DateTime.MinValue;
        }


        /// <summary>
        /// Set the selected replicate names
        /// </summary>
        /// <param name="replicateNames"></param>
        public void SetSelectedReplicateNames(ObservableCollection<string> replicateNames)
        {
            ReplicatesUserControl.SetSelected(replicateNames);
            StructuredMakeSurveyCodeAndUpdate();
        }

        /// <summary>
        /// Get a string list of the selected replicate names
        /// </summary>
        /// <returns></returns>
        public ObservableCollection<string> GetSelectedReplicateNames()
        {
            if (Structure == SurveyCodeStructure.Structured)
            {
                ObservableCollection<string> observableNames = new(ReplicatesUserControl.GetSelected());
                return observableNames;
            }
            else
                return [];
        }


        /// <summary>
        /// Used one the media file names or survey code, which should have a structured name format
        /// to help prime the site/depth and date values for the survey. This is meant to be a 
        /// hint, and the user should be able to change the values as needed. 
        /// Example inputs that are supported
        /// Media file name format: <site code>-<depth>[m]-<replicates>-<yyyy-mm-dd> allow '-' or '_' as separators.
        /// "BCW-10m-56-2024-05-01-LEFT" or "BCW_10m_56_2024-05-01_LEFT"
        /// "BO3-Slope-2024-05-01-L" or "BO3_Slope_2024-05-01_L"
        /// Survey Code: <site code>-<depth>[m]-<replicates>-<yyyy-mm-dd>
        /// "BCW-10m-56-2024-05-01" or "BCW_10m_56_2024-05-01"
        /// "BO3-Slope-2024-05-01" or "BO3_Slope_2024-05-01"
        /// </summary>
        /// <param name="mediaFileNameOrSurveyCode"></param>
        public void SetHint(string mediaFileNameOrSurveyCode)
        {
            if (Structure == SurveyCodeStructure.Structured && _fieldTrip is not null)
            {
                (bool valid, string SiteNameOrCode, string depth, ObservableCollection<string> replicates, DateTime? surveyDate, string rawCleanedHint) = HintSplitter(mediaFileNameOrSurveyCode);

                if (valid)
                {
                    if (!string.IsNullOrWhiteSpace(SiteNameOrCode))
                        SetSiteNameOrCode(SiteNameOrCode);

                    if (!string.IsNullOrWhiteSpace(depth))
                        SetDepth(depth);
                    else
                        SetDepth(string.Empty);

                    if (surveyDate is not null)
                        SetSurveyDate((DateTime)surveyDate);
                    else
                        SetSurveyDate(DateTime.Now);


                    SetSelectedReplicateNames(replicates);                    
                }
                else
                {
                    // Hint not valid so clear the fields
                    SetSiteNameOrCode(string.Empty);
                    SetDepth(string.Empty);
                    SetSurveyDate(DateTime.Now);
                    SetSelectedReplicateNames([]);
                }

                StructuredMakeSurveyCodeAndUpdate();
            }
            else
            {
                // Cleanup only
                (_, _, string depth, _, _, string rawCleanedHint) = HintSplitter(mediaFileNameOrSurveyCode);

                SurveyCode.Text = rawCleanedHint;

                // Try to apply the depth field
                if (!string.IsNullOrEmpty(depth))
                {
                    // Do we have this depth in the drop down list?
                    ComboBoxItem? item = SurveyDepth.Items.OfType<ComboBoxItem>()
                                    .FirstOrDefault(i => string.Equals(i.Content as string, depth, StringComparison.OrdinalIgnoreCase));

                    // If found set the item as the selected item in the combo box
                    if (item is not null)
                    {
                        SurveyDepth.SelectedItem = item;
                    }
                }

                UnstructuredEntryFieldsValidAndUpdate();
            }            
        }


        /// <summary>
        /// Core function to split a hint up.  This method is public to enable testing
        /// </summary>
        /// <param name="mediaFileNameOrSurveyCode"></param>
        /// <returns></returns>
        public (bool valid, string SiteNameOrCode, string depth, ObservableCollection<string> replicates, DateTime? surveyDate, string rawCleanedHint) HintSplitter(string mediaFileNameOrSurveyCode)
        {
            bool valid = true;
            string SiteNameOrCode = string.Empty;
            string depth = string.Empty;
            ObservableCollection<string> replicates = [];
            DateTime? surveyDate = null;
            string rawCleanedHint = string.Empty;

            if (string.IsNullOrWhiteSpace(mediaFileNameOrSurveyCode))
                return (false, string.Empty, string.Empty, [], null, string.Empty);


            // Remove any file extension if present, we only want the file name as the hint text
            string rawHintText = Path.GetFileNameWithoutExtension(mediaFileNameOrSurveyCode.Trim());

            // Ignore mediaFileNameOrSurveyCode that start "3D_" or "GOPR"
            if (rawHintText.StartsWith("3D_", StringComparison.OrdinalIgnoreCase) ||
                (rawHintText.Length == 8 && rawHintText.StartsWith("GOPR", StringComparison.OrdinalIgnoreCase)) ||
                (rawHintText.Length == 8 && rawHintText.StartsWith("GH", StringComparison.OrdinalIgnoreCase)))
            {
                return (false, string.Empty, string.Empty, [], null, string.Empty);
            }

            // Remove any () at the end e.g. BCW-10m-56-2024-05-01 (PART1)
            rawHintText = Regex.Replace(rawHintText, @"\s*\([^)]*\)\s*$", "", RegexOptions.IgnoreCase);

            // Remove any _L_ or _R_ or -L- or -R- and replace with '_'
            rawHintText = Regex.Replace(rawHintText, @"[_-][LR][_-]", "_", RegexOptions.IgnoreCase);

            // Remove any leading or trailing unnecessary text like "SVS_" "_LEFT", "_RIGHT", "_L",
            // "_R" that might be in the media file name but not relevant for the survey code fields
            rawHintText = Regex.Replace(rawHintText, @"^(BENTHIC[_-])|^(SVS[_-])|([_-](FILE1|FILE2|FILE3|FILE4|PART1|PART2|PART3|PART4| PART1| PART2| PART3| PART4))$", "", RegexOptions.IgnoreCase);
            rawHintText = Regex.Replace(rawHintText, @"([_-](LEFT|RIGHT|L|R))$", "", RegexOptions.IgnoreCase);
            rawCleanedHint = rawHintText;

            // In unstructured mode, just seed the survey code text.
            //???to be deleted
            //if (Mode != SurveyCodeMode.Structured || _fieldTrip is null)
            //{
            //    return (true, string.Empty, string.Empty, [], null, rawHintText);
            //}

            // Extract date from the raw hint first (supports yyyy-mm-dd or yyyy_mm_dd),
            // and return raw hint text with the matched date removed.
            rawHintText = TryExtractDateFromRawHint(rawHintText, out DateTime? hintDateFromRaw);

            if (hintDateFromRaw is null)
                valid = false;

            // Split the remaining hint in parts using both '-' and '_' as separators. We will try to
            // extract the site, depth and replicates from these parts.
            string hintText = rawHintText.Replace('_', '-');
            string[] parts = hintText
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return (false, string.Empty, string.Empty, [], null, string.Empty);


            // The first part is always the site code or name. The second part is the depth
            // (if it ends with 'm' or 'M' we will remove it). The third part is the replicates.           
            string hintSiteCodeOrName = parts[0];
            string hintDepth = parts.Length > 1 ? parts[1] : string.Empty;
            string hintReplicates = parts.Length > 2 ? parts[2] : string.Empty;
            string hintReplicatesAlt = parts.Length > 3 ? parts[3] : string.Empty;

            // Check of Depth and Replicate are combined (e.g. C1,S2,F2)
            if (hintDepth.Length == 2)
            {
                string firstChar = hintDepth[..1].ToUpper();
                string secondChar = hintDepth.Substring(1, 1);
                bool isDigit = int.TryParse(secondChar, out int transect);
                if ((firstChar == "F" || firstChar == "C" || firstChar == "S") && isDigit)
                {
                    hintDepth = firstChar;
                    hintReplicates = secondChar;
                }
            }

            // Prepare the depth
            // Remove and 'm' suffix
            // Expand F,C,S to Flat, Crest and Slope
            if (!string.IsNullOrWhiteSpace(hintDepth))
            {
                // Field clean the depth
                if (hintDepth.EndsWith("m", StringComparison.OrdinalIgnoreCase))
                    hintDepth = hintDepth[..^1];

                // Example F,C,S
                if (hintDepth == "F")
                    hintDepth = "Flat";
                else if (hintDepth == "C")
                    hintDepth = "Crest";
                else if (hintDepth == "S")
                    hintDepth = "Slope";
            }


            bool validSite = false;
            string? siteCode = null;
            if (_fieldTrip is not null)
            {
                // Site should be the site code but could be the site name.  Look both up, one needs 
                // to work or we don't have a site. This method also check that the site is valid.
                // We use the site Name to load int he drop down content field
                SiteNameOrCode = hintSiteCodeOrName;
                string? siteName = _fieldTrip.GetSiteNameFromCode(hintSiteCodeOrName);
                siteCode = _fieldTrip.GetSiteCodeFromName(hintSiteCodeOrName);

                if (!string.IsNullOrEmpty(siteName))
                {
                    SiteNameOrCode = siteName;
                    siteCode = hintSiteCodeOrName;
                    validSite = true;
                }
                else if (!string.IsNullOrEmpty(siteCode))
                {
                    SiteNameOrCode = _fieldTrip.GetSiteNameFromCode(siteCode) ?? hintSiteCodeOrName;
                    validSite = true;
                }

                // Validate the depth base on the site code
                if (validSite)
                {
                    // Depth depends on a valid site because we need to know the depth options for that site
                    // to validate the depth hint and load the depth drop down                
                    if (!string.IsNullOrWhiteSpace(hintDepth))
                    {
                        // Check the depth is valid for the site. If it is not valid we do not set it, but we
                        // also do not want to remove the depth options from the drop down because maybe the
                        // user can select a different depth that is valid. If it is valid we set it in the UI.
                        if (siteCode is not null)
                        {
                            List<string> validDepthsForSite = _fieldTrip.GetDepthList(siteCode);
                            if (validDepthsForSite.Contains(hintDepth))
                            {
                                depth = hintDepth;
                            }
                            else
                                // No extracted site name
                                valid = false;
                        }
                        else
                            // No extracted site name
                            valid = false;
                    }
                    else
                        // No extracted site name
                        valid = false;
                }
                else
                {
                    // No extracted site name
                    valid = false;

                    // Still return the depth
                    depth = hintDepth;
                }
            }
            else
            {
                // No extracted site name
                valid = false;

                // Still return the depth
                depth = hintDepth;
            }


            // Apply the extracted date if available
            if (hintDateFromRaw is not null)
                surveyDate = (DateTime)hintDateFromRaw;
            else
                // No extracted site name
                valid = false;

            // Replicates is independent of the site and depth validity because maybe the replicate names can be extracted and the user can then select the correct site and depth that matches those replicates. So we try to extract any replicate names from the hint as long as there are some defined in the field trip template, even if the site and depth from the hint are not valid.
            if (!string.IsNullOrWhiteSpace(hintReplicates))
            {
                if (!string.IsNullOrEmpty(siteCode) && _fieldTrip is not null)
                {
                    FieldTrip.DataClass.ReplicatesLayoutClass? layout = _fieldTrip.GetReplicateLayout(siteCode);
                    if (layout is not null)
                    {
                        List<string> availableReplicateNames = [.. layout.Layout
                                            .Where(x => x.ReplicateItemType == FieldTrip.DataClass.ReplicatesItemType.Replicate)
                                            .Select(x => x.ReplicateName)
                                            .Where(x => !string.IsNullOrWhiteSpace(x))
                                            .Cast<string>()
                                            .Distinct()];

                        replicates = ParseReplicateHint(hintReplicates, hintReplicatesAlt, availableReplicateNames);
                    }
                }
                else
                {
                    valid = false;
                    replicates = ParseReplicateHint(hintReplicates, hintReplicatesAlt, null);
                }
            }
            // Allow no replicates found to be still a valid return

            return (valid, SiteNameOrCode, depth, replicates, surveyDate, rawCleanedHint);


            // Try to extract the date from the raw hint text using regex. This supports dates in the format
            // yyyy-mm-dd or yyyy_mm_dd. If a date is successfully extracted, it is returned in the out parameter
            // and the matched date token is removed from the raw hint text and returned as the function result.
            // If no date is found or if the date is invalid, the original raw hint text is returned and the out
            // parameter is set to null.
            static string TryExtractDateFromRawHint(string rawText, out DateTime? date)
            {
                date = null;

                Match match = Regex.Match(rawText, @"(?<!\d)(\d{4})[-_](\d{2})[-_](\d{2})(?!\d)");
                if (!match.Success)
                    return rawText;

                if (!int.TryParse(match.Groups[1].Value, out int year)
                    || !int.TryParse(match.Groups[2].Value, out int month)
                    || !int.TryParse(match.Groups[3].Value, out int day))
                {
                    return rawText;
                }

                try
                {
                    date = new DateTime(year, month, day);

                    // Remove the matched date token and tidy leftover separators.
                    string withoutDate = rawText.Remove(match.Index, match.Length);
                    withoutDate = Regex.Replace(withoutDate, @"[-_]{2,}", "-").Trim('-', '_');

                    return withoutDate;
                }
                catch
                {
                    return rawText;
                }
            }

            // Decode the different style of transect markers
            // **Examples
            // T1 = 1
            // T456 = 4,5,6
            // 123 = 1,2,3
            // E2-E3 = E2,E3
            // N1 = N1
            // S2 = S2
            // E3 = E3
            // W1 = W1
            // N123 = N1,N2,N3
            // **Rules
            // Tn[n] -> n.[n]
            // Nn or Sn or En or Wn -> Nn or Sn or En or Wn and split using '-' or '_'
            // If only digits then split into single digits
            static ObservableCollection<string> ParseReplicateHint(string replicateText, string replicateTextAlt, List<string>? availableReplicateNames)
            {
                ObservableCollection<string> selected = [];
                char letter;
                char letterAlt;
                int digitCount = 0;

                // Check there is anything to process
                if (string.IsNullOrWhiteSpace(replicateText))
                    return selected;

                // Strip all non-letter/digits
                string normalized = new([.. replicateText.Where(char.IsLetterOrDigit)]);
                if (string.IsNullOrWhiteSpace(normalized))
                    return selected;


                // Check if n-m e.g. 1-3 or 1_3 -> 1,2,3 
                // This requires replicateText start with a single digit and replicateTextAlt has a single digit
                // Full example: BUT-10m-1-3-2024-05-01  -> replicateText=1  replicateTextAlt=3, replicates=1,2,3
                if (IsSingleDigit(replicateText) && IsSingleDigit(replicateTextAlt))
                {
                    (bool valid, int start, int stop, int step) = GetStartStopAndStep(replicateText, replicateTextAlt);
                    if (valid)
                    {
                        for (int i = start; i <= stop; i += step)
                        {
                            string candidate = i.ToString();
                            selected.Add(candidate);
                        }
                    }
                }
                // Check if Tn-m, Nn-m, Sn-m, En-m or Wn-m e.g. E1-3 or E1_3 -> E1,E2,E3, if T1-3 -> 1,2,3
                // This requires replicateText start with the letter N,S,E,W and has a digit after and replicateTextAlt has a single digit
                // Full example: BUT-10m-E1-3-2024-05-01  -> replicateText=E1  replicateTextAlt=3, replicates=E1,E2,E3
                //               BUT-10m-T1-4-2024-05-01  -> replicateText=T1  replicateTextAlt=4, replicates=1,2,3,4
                else if (IsLetterAndDigit(replicateText, out letter) && IsSingleDigit(replicateTextAlt))
                {
                    (bool valid, int start, int stop, int step) = GetStartStopAndStep(replicateText[1..], replicateTextAlt);
                    if (valid)
                    {
                        for (int i = start; i <= stop; i += step)
                        {
                            string candidate;
                            if (IsCompassLetter(letter))
                                // If the letter was N,S,E,W then we assume the candidate replicate names also have the letter
                                candidate = $"{letter}{i}";
                            else
                                // If it was T (or any other) we assume the candidate replicate names do not have the letter
                                candidate = $"{i}";
                            selected.Add(candidate);
                        }
                    }
                }
                // Check if Tn-Tm, Nn-Nm, Sn-Sm, En-Em or Wn-Wm e.g. E1-E3 or E1_E3 -> E1,E2,E3 
                // This requires replicateText and replicateTextAlt start with same the letter N,S,E,W and have a single digit after 
                // Full example: BUT-10m-E1-E3-2024-05-01  -> replicateText=E1  replicateTextAlt=E3, replicates=E1,E2,E3
                //               BUT-10m-T1-T4-2024-05-01  -> replicateText=T1  replicateTextAlt=T4, replicates=1,2,3,4
                else if (IsLetterAndDigit(replicateText, out letter) && IsLetterAndDigit(replicateTextAlt, out letterAlt))
                {
                    if (letter == letterAlt)
                    {
                        (bool valid, int start, int stop, int step) = GetStartStopAndStep(replicateText[1..], replicateTextAlt[1..]);
                        if (valid)
                        {
                            for (int i = start; i <= stop; i += step)
                            {
                                string candidate;
                                if (IsCompassLetter(letter))
                                    // If the letter was N,S,E,W then we assume the candidate replicate names also have the letter
                                    candidate = $"{letter}{i}";
                                else
                                    // If it was T (or any other) we assume the candidate replicate names do not have the letter
                                    candidate = $"{i}";
                                selected.Add(candidate);
                            }
                        }
                    }
                }
                // Check if Tn[n], Nn[n], Sn[n], En[n], Wn[n] e.g. 'E3' or 'N12'  -> E3 or N1,N2
                // This requires replicateText start with the letter N,S,E,W and have one or more digit after and replicateTextAlt is empty
                // Full example: BUT-10m-E123-2024-05-01  -> replicateText=E123  replicateTextAlt=, replicates=E1,E2,E3
                //               BUT-10m-T12-2024-05-01  -> replicateText=T12  replicateTextAlt=, replicates=1,2
                else if (IsLetterAndDigits(replicateText, out letter, out digitCount))
                {
                    for (int i = 0; i < digitCount; i++)
                    {
                        string candidate;
                        if (IsCompassLetter(letter))
                            // If the letter was N,S,E,W then we assume the candidate replicate names also have the letter
                            candidate = $"{letter}{replicateText[i + 1]}";
                        else
                            // If it was T (or any other) we assume the candidate replicate names do not have the letter
                            candidate = $"{replicateText[i + 1]}";
                        selected.Add(candidate);
                    }
                }
                // Check if just number e.g. '234'  -> 1,2,3
                // Full example: BUT-10m-123-2024-05-01  -> replicateText=123  replicateTextAlt=, replicates=1,2,3
                if (IsOnlyDigits(replicateText, out digitCount))
                {
                    for (int i = 0; i < digitCount; i++)
                    {
                        string candidate = $"{replicateText[i]}";
                        selected.Add(candidate);
                    }
                }
                else
                {
                    // Unknown format
                }

                // Next sort the list and ensure it is distinct
                selected = [.. selected.Distinct().OrderBy(x => x)];

                if (availableReplicateNames is not null)
                {
                    // Only return replicate names that are in the available replicate names for the site/depth
                    ObservableCollection<string> selectedAllowed = [];
                    foreach (string transect in selected)
                    {
                        if (availableReplicateNames.Contains(transect))
                            selectedAllowed.Add(transect);
                    }
                    return selectedAllowed;
                }
                else
                {
                    return selected;
                }
            }

            // Check if the transect number is only a single digit
            static bool IsSingleDigit(string transectName)
            {
                if (string.IsNullOrWhiteSpace(transectName))
                    return false;

                string value = transectName.Trim();
                return value.Length == 1 && char.IsDigit(value[0]);
            }

            // Check if the transect name is a letter followed by a single digit
            static bool IsLetterAndDigit(string transectName, out char letter)
            {
                letter = '\0';

                if (string.IsNullOrWhiteSpace(transectName))
                    return false;

                string text = transectName.Trim();
                if (text.Length != 2)
                    return false;

                if (!char.IsLetter(text[0]) || !char.IsDigit(text[1]))
                    return false;

                letter = char.ToUpperInvariant(text[0]);
                return true;
            }

            // Check if the transect name is a letter followed by digits e.g. E1 or N12
            // Letter return is always uppercase. 
            static bool IsLetterAndDigits(string transectName, out char letter, out int digitCount)
            {
                letter = '\0';
                digitCount = 0;

                if (string.IsNullOrWhiteSpace(transectName))
                    return false;

                string text = transectName.Trim();
                if (text.Length < 2)
                    return false;

                if (!char.IsLetter(text[0]))
                    return false;

                digitCount = text.Length - 1;
                for (int i = 1; i < text.Length; i++)
                {
                    if (!char.IsDigit(text[i]))
                        return false;
                }

                letter = char.ToUpperInvariant(text[0]);
                return true;
            }

            // Check if the transect name is only digits e.g. 123
            static bool IsOnlyDigits(string transectName, out int digitCount)
            {
                digitCount = 0;

                if (string.IsNullOrWhiteSpace(transectName))
                    return false;

                string text = transectName.Trim();
                if (text.Length == 0)
                    return false;

                digitCount = text.Length;
                for (int i = 0; i < text.Length; i++)
                {
                    if (!char.IsDigit(text[i]))
                        return false;
                }

                return true; 
            }

            // Check if the letter is a compass letter i.e. N,S,E,W
            static bool IsCompassLetter(char letter)
            {
                if (letter == 'N' || letter == 'S' || letter == 'E' || letter == 'W' ||
                    letter == 'n' || letter == 's' || letter == 'e' || letter == 'w')
                    return true;
                else
                    return false;  
            }

            // Generate the start/end and step (+1 or -1)
            static (bool valid, int start, int stop, int step) GetStartStopAndStep(string transectNumber1, string transectNumber2)
            {
                bool valid = true;
                int start = 0;
                int stop = 0;
                int step = 0;

                if (int.TryParse(transectNumber1, out start))
                {
                    if (int.TryParse(transectNumber2, out stop))
                    {
                        if (start <= stop)
                            step = 1;
                        else
                            step = -1;
                    }
                    else
                        valid = false;
                }
                else
                    valid = false;

                return (valid, start, stop, step);
            }
        }

        /// <summary>
        /// USed to setup the entry fields based on a survey code. If the input is structured the survey code
        /// will be parsed and the individual fields (site, depth, replicates, date) will be set accordingly.
        /// If the input is unstructured then the survey code will just be set as the text in the survey code 
        /// field for the user to edit as needed.
        /// </summary>
        /// <param name="surveyCode"></param>
        public void SetSurveyCode(string surveyCode)
        {
            if (Structure == SurveyCodeStructure.Structured)
            {
                // Structured entry
                // Use the hint function to dismantle the survey code into site, depth, replicates and date and set those fields in the UI.
                // This allows the user to then adjust any of the fields as allowed. 
                SetHint(surveyCode);
            }
            else
            {
                // Unstructured entry
                SurveyCode.Text = surveyCode;
            }
        }


        /// <summary>
        /// Return the survey code and if it is ready or not. The survey code is ready if the user has 
        /// selected a site, depth and at least one replicate.
        /// null is returned if nothing has been selected, false is returned if something has been selected 
        /// but the survey code is not ready and true is returned if all necessary inputs have been selected 
        /// and the survey code is ready. The null vs false distinction is useful so the user isn't unnecessarily 
        /// prompted that there is an input problem.
        /// </summary>
        /// <returns></returns>
        public (bool? ready, string surveyCode) GetSurveyCode()
        {
            if (Structure == SurveyCodeStructure.Structured)
                return StructuredMakeSurveyCode();
            else
            {
                EntryFieldsValidReturn ret = UnstructuredEntryFieldsValid(reportIssues: false);

                bool? ready;
                if (ret == EntryFieldsValidReturn.Valid)
                    ready = true;
                else if (ret == EntryFieldsValidReturn.Invalid)
                    ready = false;
                else /*EntryFieldsValidReturn.Warning*/
                    ready = true;

                return (ready, SurveyCode.Text);
            }
        }


        /// <summary>
        /// Return a List<string> of the users selected transect names from the possible replicates 
        /// </summary>
        /// <returns></returns>
        public List<string> GetAllowedReplicateNames()
        {
            if (Structure == SurveyCodeStructure.Structured)
                return ReplicatesUserControl.GetSelected();
            else
                return [];
        }


        ///
        /// EVENTS
        /// 

        /// <summary>
        /// User has selected replicates (one or many)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReplicatesUserControl_SelectionChanged(object sender, RoutedEventArgs e)
        {
            StructuredMakeSurveyCodeAndUpdate();
        }


        /// <summary>
        /// Users can selected a site
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SiteItem_Clicked(object sender, RoutedEventArgs e)
        {
            // Guard
            if (_fieldTrip is null)
                return;

            // Get the selected site
            string? selectedSiteName = (sender as MenuFlyoutItem)?.Text;
            SetSite(selectedSiteName);

            // Load the depth for this site
            if (selectedSiteName is not null)
            {
                string? siteCode = _fieldTrip.GetSiteCodeFromName(selectedSiteName) ?? "";
                LoadDepthBasedOnSite(siteCode);
            }
            else
                LoadDepthBasedOnSite(null);
        }


        /// <summary>
        /// User has selected a depth
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DepthItem_Clicked(object sender, RoutedEventArgs e)
        {
            // Get the selected site
            string? selectedDepth = (sender as MenuFlyoutItem)?.Text;
            if (selectedDepth != null)
            {
                DepthDropDown.Content = selectedDepth;
            }

            StructuredMakeSurveyCodeAndUpdate();
        }


        /// <summary>
        /// Validate the buttons if the user has edited value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyCode_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            UnstructuredEntryFieldsValidAndUpdate();
        }


        /// <summary>
        /// Validate the buttons if the user has edited value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UnstructuredEntryFieldsValidAndUpdate();
        }


        /// <summary>
        /// Validate the buttons if the user has edited value in the dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_TextSubmitted(object sender, Microsoft.UI.Xaml.Controls.ComboBoxTextSubmittedEventArgs e)
        {
            UnstructuredEntryFieldsValidAndUpdate();
        }


        /// <summary>
        /// Wire up the TextChanged on the child TextBox. 
        /// Note. I tried to use the Loaded event but SurveyDepthTextBox_TextChanged 
        /// was never called so I switch to GotFocus
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepth_GettingFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var textBox = UIHelper.FindDescendant<TextBox>(SurveyDepth);
            if (textBox is not null)
            {
                textBox.TextChanged -= SurveyDepthTextBox_TextChanged;
                textBox.TextChanged += SurveyDepthTextBox_TextChanged;
            }
        }


        /// <summary>
        /// This had to be manually wired up because there is currently no TextChanged event on a Combo UIControl
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SurveyDepthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UnstructuredEntryFieldsValidAndUpdate();
        }



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// 
        /// </summary>

        private void ApplyMode()
        {
            //???
            //Debug.WriteLine($"SurveyCodeUserControl.ApplyMode = {Mode}, Structure = {Structure}");

            if (Structure == SurveyCodeStructure.Structured)
            {
                // Named controls exist after InitializeComponent; null-guard for safety.
                if (SiteDropDown is null || DepthDropDown is null || SurveyDatePicker is null || ReplicatesUserControl is null)
                    return;
                
                switch (Mode)
                {
                    case SurveyCodeMode.Setup:
                        SiteDropDown.IsEnabled = true;
                        //DepthDropDown.IsEnabled = true;  // Driven by the Site code selection
                        SurveyDatePicker.IsEnabled = true; 
                        ReplicatesUserControl.IsEnabled = true;
                        break;

                    case SurveyCodeMode.LimitedEdit:
                        SiteDropDown.IsEnabled = false;
                        DepthDropDown.IsEnabled = false;
                        SurveyDatePicker.IsEnabled = false;
                        ReplicatesUserControl.IsEnabled = true;
                        break;

                    case SurveyCodeMode.View:
                        SiteDropDown.IsEnabled = false;
                        DepthDropDown.IsEnabled = false;
                        SurveyDatePicker.IsEnabled = false;
                        ReplicatesUserControl.IsEnabled = false;
                        break;
                }
            }
            else /* SurveyCodeStructure.Unstructured */
            {
                // Named controls exist after InitializeComponent; null-guard for safety.
                if (SurveyCode is null || SurveyDepth is null)
                    return;

                switch (Mode)
                {
                    case SurveyCodeMode.Setup:
                        SurveyCode.IsEnabled = true;
                        SurveyDepth.IsEnabled = true;
                        break;

                    case SurveyCodeMode.LimitedEdit:    // There is no Limited edit concept for Structure=Unstructured
                    case SurveyCodeMode.View:
                        SurveyCode.IsEnabled = false;
                        SurveyDepth.IsEnabled = false;
                        break;
                }
            }
        }

        /// <summary>
        /// Comes from .XAML depth list for now. Move into Settings so I can be adjusted in the field
        /// </summary>
        private void LoadDefaultDepthList()
        {
            // TO DO
        }

        /// <summary>
        /// Loads depth information based on the specified site code.
        /// Set to null to clear and disable selection
        /// </summary>
        /// <param name="siteCode">The code identifying the site for which to load depth information. May be null to indicate that no site is
        /// selected.</param>
        private void LoadDepthBasedOnSite(string? siteCode)
        {
            if (siteCode is null)
            {
                DepthDropDown.IsEnabled = false;
                DepthDropDown.Content = _depthSelectorOriginalText;
                DepthMenuFlyout.Items.Clear();
            }
            else
            {
                DepthDropDown.IsEnabled = true;
                DepthMenuFlyout.Items.Clear();

                if (_fieldTrip is not null)
                {
                    List<string> depths = _fieldTrip.GetDepthList(siteCode);
                    foreach (string depth in depths)
                    {
                        MenuFlyoutItem item = new() { Text = depth };
                        DepthMenuFlyout.Items.Add(item);

                        // Add a Clicked callback to each item
                        item.Click += DepthItem_Clicked;
                    }
                }
            }
        }

        /// <summary>
        /// Load Sites selector
        /// </summary>
        /// <param name="siteCode"></param>
        private void LoadSites()
        {
            if (_fieldTrip is not null)
            {
                // Clear any existing items
                SiteMenuFlyout.Items.Clear();

                // Load the Site Selector
                List<string> sites = _fieldTrip.GetSiteNameList();
                foreach (string site in sites)
                {
                    MenuFlyoutItem item = new() { Text = site };
                    SiteMenuFlyout.Items.Add(item);

                    // Add a Clicked callback to each item
                    item.Click += SiteItem_Clicked;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="siteName"></param>
        private void SetSite(string? siteName)
        {
            if (siteName != null)
            {
                SiteDropDown.Content = siteName;
                LoadDepthBasedOnSite(siteName);
            }
            else
            {
                SiteDropDown.Content = _siteSelectorOriginalText;
                LoadDepthBasedOnSite(null);
            }

            if (_fieldTrip is not null)
            {
                if (!_fieldTrip.IsAllReplicatesSameSetup() && siteName is not null)
                {
                    string? siteCode = _fieldTrip.GetSiteCodeFromName(siteName);
                    if (siteCode is not null)
                    {
                        ReplicatesUserControl.SetReplicateLayout(siteCode);
                        StructuredVisibility = Visibility.Visible;
                        StructuredVisibilityReplicates = Visibility.Visible;                        
                    }
                }
                else
                {
                    StructuredVisibility = Visibility.Visible;
                    
                    if (!string.IsNullOrEmpty(siteName))
                        // If the site is selected then display the replicates user control
                        StructuredVisibilityReplicates = Visibility.Visible;
                    else
                        // Else hide the replicates user control until a site is selected later
                        StructuredVisibilityReplicates = Visibility.Collapsed;
                }
            }

            StructuredMakeSurveyCodeAndUpdate();
        }


        /// <summary>
        /// Build the survey code from it component input controls and update the display. 
        /// Also check if the survey code is ready or not and send a SelectionChanged event 
        /// to notify the parent control of the change in status. This allows the parent control to decide if the 
        /// survey can be started or not (e.g. enable/disable a button)
        /// </summary>
        private bool? StructuredMakeSurveyCodeAndUpdate()
        {
            (bool? surveyCodeReady, string surveyCode) = StructuredMakeSurveyCode();

            SurveyCodeTextBlock.Text = surveyCode;

            // Display the Survey Code status
            if (surveyCodeReady == true)
            {
                SurveyCodeGlyph.Visibility = Visibility.Visible;
                SurveyCodeGlyph.Glyph = "\uE73E";     // Tick
                try
                {
                    var themeBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                    SurveyCodeGlyph.Foreground = themeBrush;
                }
                catch { }
            }
            else if (surveyCodeReady is null)
            {
                SurveyCodeGlyph.Visibility = Visibility.Collapsed;
            }
            else /*surveyCodeReady == false*/
            {
                SurveyCodeGlyph.Visibility = Visibility.Visible;
                SurveyCodeGlyph.Glyph = "\uE783";     // Warning
                try
                {
                    var themeBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    SurveyCodeGlyph.Foreground = themeBrush;
                }
                catch { }
            }

            // Signal that the selection has changed so the parent control can check if the survey code
            // is ready or not and update the UI accordingly 
            SelectionChanged?.Invoke(this, null);

            return surveyCodeReady;
        }


        /// <summary>
        /// Build the survey code from it component input controls and return the code and if it is ready or not. 
        /// The survey code is ready if the user has selected a site, depth and at least one replicate. 
        /// The survey code is built in the format: <site code>-<depth>[m]-<replicates]-<yyyy-mm-dd>. 
        /// For example: "BCW-10m-56-2024-05-01" or "BO3-Slope-2024-05-01"
        /// If nothing has been selected ready is returned as null
        /// If something has been selected but the survey code is not ready then ready is returned as false. 
        /// If all necessary inputs have been selected and the survey code is ready then ready is returned as true.
        /// The null vs false distinction is useful so the user isn't unnecessarily prompted that there is an input problem
        /// </summary>
        /// <returns>bool? ready</returns>
        /// <returns>string surveyCode</returns>
        private (bool? ready, string surveyCode) StructuredMakeSurveyCode()
        {
            bool? isSurveyCodeReady = false;
            string surveyCode = string.Empty;

            if (_fieldTrip is not null)
            {
                bool isSiteCodeReady = false;
                bool isDepthReady = false;
                bool isReplicatesReady = false;

                // Get the selected site and depth
                string siteName = SiteDropDown.Content as string ?? "";
                string depthName = DepthDropDown.Content as string ?? "";
                string surveyDate = SurveyDatePicker.Date.ToString("yyyy-MM-dd");
                List<string> selectedReplicates = ReplicatesUserControl.GetSelected();

                // Remove default values
                if (siteName == "Site")
                    siteName = string.Empty;
                if (depthName == "Depth")
                    depthName = string.Empty;

                // Get the Site Code
                string? siteCode = _fieldTrip.GetSiteCodeFromName(siteName) ?? "";

                // Apply the Survey Code
                StringBuilder sb = new();
                if (!string.IsNullOrEmpty(siteCode))
                {
                    sb.Append(siteCode);
                    sb.Append('-');
                    isSiteCodeReady = true;
                }
                else
                    sb.Append("<site>-");

                // Apply the Depth
                if (!string.IsNullOrEmpty(depthName))
                {
                    if (int.TryParse(depthName, out int depthNumber) && depthNumber > 0)
                    {
                        // Assume the depth is in meters (so add a 'm' suffix)
                        sb.Append(depthName);
                        sb.Append("m-");
                    }
                    else
                    {
                        // Assume the depth is a string like 'slope', 'flat' or 'crest' (so don't add a 'm' suffix)
                        sb.Append(depthName);
                        sb.Append('-');
                    }
                    isDepthReady = true;
                }
                else
                    sb.Append("<depth>m-");

                // Apply the replicates
                string replicatesText = string.Join("", selectedReplicates);                

                if (!string.IsNullOrEmpty(replicatesText))
                {
                    sb.Append(replicatesText);
                    sb.Append('-');
                    isReplicatesReady = true;
                }
                else
                    sb.Append("<replicates>-");

                // Apply the date
                sb.Append(surveyDate);

                // Get the built up Survey Code
                surveyCode = sb.ToString();

                // Display the Survey Code status
                if (isSiteCodeReady && isDepthReady && isReplicatesReady)
                {
                    isSurveyCodeReady = true;
                }
                else if (!isSiteCodeReady && !isDepthReady && !isReplicatesReady)
                {
                    isSurveyCodeReady = null;
                }
                else
                {
                    isSurveyCodeReady = false;
                }               
            }

            return (isSurveyCodeReady, surveyCode);
        }


        /// <summary>
        /// Called when anything change to test the validity of the survey information and media
        /// This is also shows on the users control which fields are invalid
        /// </summary>
        /// <returns></returns>
        /// 
        enum EntryFieldsValidReturn
        {
            Invalid,
            Valid,
            Warning
        }
        private EntryFieldsValidReturn UnstructuredEntryFieldsValid(bool reportIssues)
        {
            EntryFieldsValidReturn ret = EntryFieldsValidReturn.Valid;
            bool infoValid = true;

            // Check survey code
            string surveyCode = SurveyCode.Text;
            if (!IsFileNameValid(surveyCode))
            {
                SetValidationText(false/*invalid*/, null, SurveyCodeValidationGlyph, SurveyCodeValidationText, @"Can't contain < > : \ / | ? *", "");
                infoValid = false;

                //???if (reportIssues)
                //    report?.Warning("", $"The survey code:{surveyCode} contains invalid characters");
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyCodeValidationGlyph, SurveyCodeValidationText, "", "");


            // Check survey depth
            string? surveyDepth;

            if (SurveyDepth.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content != null)
            {
                surveyDepth = selectedItem.Content.ToString();
            }
            else
            {
                // For custom user input
                surveyDepth = SurveyDepth.Text;
            }

            if (string.IsNullOrWhiteSpace(surveyDepth))
            {
                SetValidationText(false/*invalid*/, null, SurveyDepthValidationGlyph, SurveyDepthValidationText, "Survey depth must have a value", "");
                infoValid = false;

                //???if (reportIssues)
                //???    report?.Warning("", $"The survey depth for survey {surveyCode} is missing");
            }
            else
                SetValidationText(null/*nothing*/, null, SurveyDepthValidationGlyph, SurveyDepthValidationText, "", "");


            // Return Invalid if any invalid data
            if (!infoValid)
                ret = EntryFieldsValidReturn.Invalid;

            return ret;
        }

        /// <summary>
        /// Called check validity and fire a changed event to the parent
        /// </summary>
        private void UnstructuredEntryFieldsValidAndUpdate()
        {
            UnstructuredEntryFieldsValid(false/*no reporting*/);

            // Signal that the selection has changed so the parent control can check if the survey code
            // is ready or not and update the UI accordingly 
            SelectionChanged?.Invoke(this, null);
        }

        /// <summary>
        /// Called to set the validation test and icon status
        /// </summary>
        /// <param name="validTRUEInvalidFALSE"></param>
        /// <param name="glyph"></param>
        /// <param name="validationText"></param>
        /// <param name="text"></param>
        private static void SetValidationText(bool? validTRUEInvalidFALSE, StackPanel? panel, FontIcon glyph, TextBlock validationText, string text, string tooltip)
        {
            if (validTRUEInvalidFALSE is null)
            {
                if (panel is not null)
                    panel.Visibility = Visibility.Collapsed;

                glyph.Glyph = "";
                validationText.Text = "";
            }
            else if ((bool)validTRUEInvalidFALSE == true)
            {
                // Get the brush from the application resources
                var themeBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                if (panel is not null)
                    panel.Visibility = Visibility.Visible;

                glyph.Glyph = "\uE73E";     // Tick
                glyph.Foreground = themeBrush;
                validationText.Text = text;
            }
            else
            {
                // Get the brush from the application resources
                var themeBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

                if (panel is not null)
                    panel.Visibility = Visibility.Visible;

                glyph.Glyph = "\uE783";    // Information 
                glyph.Foreground = themeBrush;
                validationText.Text = text;
            }

            // Retrieve the tool tip programmatically
            bool applyTooltip = false;

            if (ToolTipService.GetToolTip(validationText) is not ToolTip existingToolTip)
            {
                applyTooltip = true;
            }
            else if ((string)existingToolTip.Content != tooltip)
            {
                // Update tool tip
                existingToolTip.Content = tooltip;
            }

            // Change the tool tip
            if (applyTooltip)
            {
                ToolTip toolTip = new() { Content = tooltip };
                ToolTipService.SetToolTip(validationText, toolTip);
            }
        }

        /// <summary>
        /// Test if a string has characters that are valid for use in the file name
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static bool IsFileNameValid(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            // Get invalid characters from Path
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Check if fileName contains any invalid character
            return !fileName.Any(c => invalidChars.Contains(c));
        }


    }
}
