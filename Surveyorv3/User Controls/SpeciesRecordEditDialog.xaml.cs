// SpeciesRecordEditDialog  Mananges the cached species image file
//
// Version 1.0 20 Apr 2025

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.User_Controls;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Surveyor.Helper;
using static Surveyor.Helper.HtmlFishBaseParser;
using static Surveyor.SpeciesItem;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;



namespace Surveyor
{
    public sealed partial class SpeciesRecordEditDialog : UserControl
    {
        private readonly Reporter? report;

        private SpeciesItem speciesItem = new();
        private bool? editExisting = null;
        private int atStepNumber = 0;

        private ContentDialog? dialog = null;
        private HttpClient httpClient = new();
        private HtmlFishBaseSpeciesMetadata? fishBaseSpeciesMetadata = null;
        private bool? pageDownloadedAndExtractedOk = null;

        public SpeciesRecordEditDialog(Reporter? _report)
        {
            report = _report;

            this.InitializeComponent();
        }


        /// <summary>
        /// Create a new species record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        internal async Task<bool> SpeciesRecordNew(Window settingsWindow, SpeciesItem speciesItem, SpeciesCodeList speciesCodeList)
        {
            return await SpeciesRecordEditorNew(settingsWindow, speciesItem, false/*editExisting*/, speciesCodeList);
        }


        /// <summary>
        /// Exist an existing species info record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        internal async Task<bool> SpeciesRecordEdit(Window settingsWindow, SpeciesItem speciesItem, SpeciesCodeList speciesCodeList)
        {
            return await SpeciesRecordEditorNew(settingsWindow, speciesItem, true/*editExisting*/, speciesCodeList);
        }



        ///
        /// EVENTS
        /// 



        private void SpeciesLatin_TextChanged(object sender, TextChangedEventArgs e)
        {
            EnforceLowercaseTextBox((TextBox)sender);

            ManageControlVisability();
        }

        private void SpeciesCommon_TextChanged(object sender, TextChangedEventArgs e)
        {
            ManageControlVisability();
        }

        private void GenusLatin_TextChanged(object sender, TextChangedEventArgs e)
        {
            ManageControlVisability();
        }

        private void FamilyAutoSuggest_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            ManageControlVisability();
        }

        private void FamilyAutoSuggest_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            ManageControlVisability();
        }

        private void FamilyAutoSuggest_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ManageControlVisability();
        }

        /// <summary>
        /// User press the 'Go To FishBase' button.
        /// Try to go to the summury page for this species
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FishbaseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            bool ret;
            string genus = GenusLatin.Text;
            string species = SpeciesLatin.Text;

            FishBaseID.Text = "";

            ret = await OpenFishBasePageAsync(genus, species);
            if (ret == true)
            {
                atStepNumber = 2;
                ManageControlVisability();
            }
        }

        private void FishbaseURL_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = FishBaseURL.Text;
            if (IsValidUrl(url))
                FishBaseURLOKButton.IsEnabled = true;
            else
                FishBaseURLOKButton.IsEnabled = false;
        }

        private async void FishbaseURLDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            // Download the provided URL. We'll assume is it a species summary page 
            // and try to extract the FishBase ID (known as speccode in the html) and
            // the family,genus and species names from the page
            
            string url = FishBaseURL.Text;
            string localFileSpec = url.GetHashCode().ToString("X") + ".html";

            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();

                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(localFileSpec, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, content);

                try
                {
                    HtmlFishBaseSpeciesMetadata? fishBaseSpeciesMetadata = await ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(file);

                    if (fishBaseSpeciesMetadata is not null)
                    {
                        GenusSpeciesConfirmText.Text = $"Family: {fishBaseSpeciesMetadata.FamilyLatin}\nGenus: {fishBaseSpeciesMetadata.Genus}\nSpecies: {fishBaseSpeciesMetadata.SpeciesLatin}";

                        if (fishBaseSpeciesMetadata.FishID is not null)
                        {
                            pageDownloadedAndExtractedOk = true;
                            SetFishBaseURLValid(true/*trueTickFalseCrossNullNothing*/);
                        }

                        atStepNumber = 3;
                        ManageControlVisability();

                    }

                    // Clean up
                    await file.DeleteAsync();
                }
                catch (Exception ex)
                {
                    report?.Warning("", $"Failed to extract Fish ID fron the FishBase page {url}, {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                report?.Warning("", $"Failed to get FishBase page {url}, {ex.Message}");
            }           
        }


        /// <summary>
        /// User confirmed the family/genus/species extracted from FishBase.org matches
        /// the species code list record
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GenusSpeciesConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (fishBaseSpeciesMetadata is not null && fishBaseSpeciesMetadata.FishID is not null)
            {
                int fishID = (int)fishBaseSpeciesMetadata.FishID;

                speciesItem.Code = SpeciesItem.MakeCodeFromCodeTypeAndCode(CodeType.FishBase, fishID.ToString());
                FishBaseID.Text = $"{fishBaseSpeciesMetadata.FishID}";

                atStepNumber = 4;
                ManageControlVisability();
            }
        }



        ///
        /// PRIVATE
        ///

        /// <summary>
        /// Handle the edit or new (create) species info dialog
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="editExisting"></param>
        /// <returns>true is the speciesInfo parameter has been changed</returns>
        private async Task<bool> SpeciesRecordEditorNew(Window settingsWindow, SpeciesItem _speciesItem, bool _editExisting, SpeciesCodeList speciesCodeList)
        {
            bool ret = false;
            string title = "";
            string primaryButtonText;

            try
            {
                editExisting = _editExisting;

                ClearControls();

                if ((bool)editExisting)
                {
                    title = "Edit Species Record";
                    primaryButtonText = "Update";

                    speciesItem = _speciesItem;

                    // Populate the controls with the existing species info
                    Family.Text = speciesItem.Family;
                    GenusLatin.Text = speciesItem.Genus;
                    SpeciesLatin.Text = speciesItem.SpeciesScientific;
                    SpeciesCommon.Text = speciesItem.SpeciesCommon;
                    SpeciesCode.Text = speciesItem.Code;
                    FishBaseURL.Text = string.Empty;
                    FishBaseID.Text = string.Empty;
                    pageDownloadedAndExtractedOk = null;

                    if (IsFishBaseIDSetup(speciesItem.Code))
                    {
                        atStepNumber = 4;
                    }
                    else
                    {
                        atStepNumber = 1;
                    }
                }
                else
                {
                    title = "New Species Record";
                    primaryButtonText = "Add";

                    Family.Text = string.Empty;
                    GenusLatin.Text = string.Empty;
                    SpeciesLatin.Text = string.Empty;
                    SpeciesCommon.Text = string.Empty;
                    SpeciesCode.Text = string.Empty;
                    FishBaseURL.Text = string.Empty;
                    FishBaseID.Text = string.Empty;
                    pageDownloadedAndExtractedOk = null;

                    atStepNumber = 0;
                }

                // Populate the family auto suggest
                Family.TextMemberPath = "Family";
                Family.DisplayMemberPath = "Family";
                speciesCodeList.SearchFamily("");
                Family.ItemsSource = speciesCodeList.FamilyComboItems;

                // Create the dialog
                dialog = new()
                {
                    Title = title,
                    CloseButtonText = "Cancel",
                    PrimaryButtonText = primaryButtonText,
                    MaxWidth = 800, // <-- important!                
                    XamlRoot = settingsWindow.Content.XamlRoot  // Set the XamlRoot property
                };

                // Wrap the control in a ScrollViewer here:
                var contentWrapper = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 600, // or Window.Current.Bounds.Height * 0.8
                    Content = this  // 'this' is the UserControl
                };

                dialog.Content = contentWrapper;


                // Hook up closed cleanup handler
                dialog.Closed += (s, e) =>
                {
                    ClearControls();
                };

                // Enable the buttons
                ManageControlVisability();

                // Show the dialog and handle the response
                var result = await dialog.ShowAsync();
                dialog.Content = null;  // Detach the content after the dialog is closed

                // Check if the Add button pressed
                if (result == ContentDialogResult.Primary)
                {
                    ret = true;
                }
                else
                {

                    ret = false;
                }
            }
            catch (Exception ex)
            {
                report?.Debug("", $"SpeciesRecordEditDialog.SpeciesRecordEditorNew, Failed {ex.Message}");
            }

            dialog = null;

            return ret;
        }




        private void ClearControls()
        {
            fishBaseSpeciesMetadata = null;
        }


        /// <summary>
        /// Manage the states of the UIelements
        /// </summary>
        private void ManageControlVisability()
        {
            if (dialog is not null)
            {
                // Check if enough data is entered to enable the Add or Update button
                if (!string.IsNullOrEmpty(Family.Text) &&
                    !string.IsNullOrEmpty(GenusLatin.Text) &&
                    !string.IsNullOrEmpty(SpeciesLatin.Text))
                {
                    dialog.IsPrimaryButtonEnabled = true;
                    if (atStepNumber == 0)
                        atStepNumber = 1;
                }
                else
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    atStepNumber = 0;
                }

                // Disable text to indicate that this record is connected to FishBase
                if (IsFishBaseIDSetup(speciesItem.Code))
                {
                    SpeciesFishIDSetupOK.Visibility = Visibility.Visible;
                }
                else
                {
                    SpeciesFishIDSetupOK.Visibility = Visibility.Collapsed;

                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    {
                        // We have internet
                        SpeciesFishIDNoInternet.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // No internet available to ask user to return to this dialog later
                        SpeciesFishIDNoInternet.Visibility = Visibility.Visible;
                    }
                }


                // Show the steps to connect this species code list record to FishBase
                if (atStepNumber == 0)
                {
                    // Nothing - no step shown
                    SpeciesFishIDInstructionHeader.Visibility = Visibility.Collapsed;
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Collapsed, 0/*fromRowIndex*/, 7/*toRowIndex*/);
                    SetFishBaseURLValid(null);
                }
                else if (atStepNumber == 1)
                {
                    // Step 1 - Try to open FishBase summary page
                    SpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Visible, 0/*fromRowIndex*/, 1/*toRowIndex*/);
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Collapsed, 2/*fromRowIndex*/, 7/*toRowIndex*/);
                    SetFishBaseURLValid(pageDownloadedAndExtractedOk);
                }
                else if (atStepNumber == 2)
                {
                    // Step 2 - Paste in the URL of the correct FishBase summary page. User press a button to download
                    // the page and the software extracts key items of data including the FishBase ID
                    SpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Visible, 0/*fromRowIndex*/, 3/*toRowIndex*/);
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Collapsed, 4/*fromRowIndex*/, 7/*toRowIndex*/);
                    SetFishBaseURLValid(pageDownloadedAndExtractedOk);
                }
                else if (atStepNumber == 3)
                {
                    // Step 3 - User confirms the Fish's Species/Genus/Family from the FishBase summary page
                    // matched the species we are setting up
                    SpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Visible, 0/*fromRowIndex*/, 6/*toRowIndex*/);
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Collapsed, 7/*fromRowIndex*/, 7/*toRowIndex*/);
                    SetFishBaseURLValid(pageDownloadedAndExtractedOk);
                }
                else if (atStepNumber == 4)
                {
                    // Step 4 - The acquired FishBase fishID is deplayed
                    SpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                    GridRowsVisibility(LinkToFishBaseGrid, Visibility.Visible, 0/*fromRowIndex*/, 7/*toRowIndex*/);
                    SetFishBaseURLValid(pageDownloadedAndExtractedOk);
                }


                string url = FishBaseURL.Text;
                if (IsValidUrl(url) && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    FishBaseURLOKButton.IsEnabled = true;
                else
                    FishBaseURLOKButton.IsEnabled = false;
            }
        }


        /// <summary>
        /// Open the FishBase species summary page for this genus/species.
        /// If the page 404s default to the FishBase search page
        /// </summary>
        /// <param name="genus"></param>
        /// <param name="species"></param>
        /// <returns></returns>

        private async Task<bool> OpenFishBasePageAsync(string genus, string species)
        {
            bool ret = false;

            string baseUrl = "https://www.fishbase.se";
            string summaryUrl = $"{baseUrl}/summary/{genus}_{species}.html";
            string fallbackSearchUrl = $"{baseUrl}/search.php";

            try
            {           
                using var response = await httpClient.GetAsync(summaryUrl);

                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();

                    if (html.Contains("<h1>Not Found</h1>", StringComparison.OrdinalIgnoreCase))
                    {
                        // Fake 404 — redirect to fallback
                        await Launcher.LaunchUriAsync(new Uri(fallbackSearchUrl));
                    }
                    else
                    {
                        // Page exists and looks valid
                        await Launcher.LaunchUriAsync(new Uri(summaryUrl));
                    }
                }
                else
                {
                    // Real HTTP error — fallback
                    await Launcher.LaunchUriAsync(new Uri(fallbackSearchUrl));
                }

                ret = true;
            }
            catch (Exception)
            {
                await Launcher.LaunchUriAsync(new Uri(fallbackSearchUrl));
            }

            return ret;
        }


        /// <summary>
        /// Check if the FishBase ID is set up
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        private bool IsFishBaseIDSetup(string code)
        {
            string FishID = speciesItem.ExtractID();
            if (speciesItem.ExtractCodeType() == SpeciesItem.CodeType.FishBase && !string.IsNullOrEmpty(FishID))
                return true;
            else
                return false;
        }


        /// <summary>
        /// Hide or show a range of rows in a grid
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="visibility"></param>
        /// <param name="fromRowIndex"></param>
        /// <param name="toRowIndex"></param>
        private void GridRowsVisibility(Grid grid, Visibility visibility, int fromRowIndex, int toRowIndex)
        {
            foreach (var child in grid.Children)
            {
                if (child is FrameworkElement element)
                {
                    int rowIndex = Grid.GetRow(element);
                    if (rowIndex >= fromRowIndex && rowIndex <= toRowIndex)
                    {
                        element.Visibility = visibility;
                    }
                }
            }
        }


        /// <summary>
        /// CHeck if a URL is correctly formed
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }



        /// <summary>
        /// Enforce lower case on the text box
        /// </summary>
        private bool _suppressTextChange = false;

        private void EnforceLowercaseTextBox(TextBox textBox)
        {
            if (_suppressTextChange) return;

            var originalText = textBox.Text;
            var lowerText = originalText.ToLower();

            if (originalText != lowerText)
            {
                var selectionStart = textBox.SelectionStart;
                _suppressTextChange = true;
                textBox.Text = lowerText;
                textBox.SelectionStart = selectionStart;
                _suppressTextChange = false;
            }
        }


        /// <summary>
        /// Set a green tick or a red cross or nothing near the FishBase URL
        /// so should it downloaded ok or not
        /// </summary>
        /// <param name="trueTickFalseCrossNullNothing"></param>
        private void SetFishBaseURLValid(bool? trueTickFalseCrossNullNothing)
        {
            if (trueTickFalseCrossNullNothing is null)
            {
                FishBaseURLValid.Glyph = string.Empty;
            }
            else if ((bool)trueTickFalseCrossNullNothing)
            {
                // Tick
                FishBaseURLValid.Glyph = "\uE73E"; // glyph 'Check'
                FishBaseURLValid.Foreground = new SolidColorBrush(Colors.Green);
                FishBaseURLValid.Visibility = Visibility.Visible;
            }
            else
            {
                // Cross
                FishBaseURLValid.Glyph = "\uE711"; // glyph 'Close'
                FishBaseURLValid.Foreground = new SolidColorBrush(Colors.Red);
                FishBaseURLValid.Visibility = Visibility.Visible;
            }
        }


    }
}
