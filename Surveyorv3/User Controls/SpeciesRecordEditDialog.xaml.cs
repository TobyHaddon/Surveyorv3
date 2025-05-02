// SpeciesRecordEditDialog  Mananges the cached species image file
//
// Version 1.0 20 Apr 2025

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.User_Controls;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Surveyor.Helper;
using static Surveyor.Helper.HtmlFishBaseParser;
using static Surveyor.SpeciesItem;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System.ComponentModel.Design;
using System.Diagnostics;
using Windows.UI.Core;
using Microsoft.UI.Input;



namespace Surveyor
{
    public sealed partial class SpeciesRecordEditDialog : UserControl
    {
        private readonly Reporter? report;

        private SpeciesItem speciesItem = new();
        private bool? editExisting = null;
        private int atStepNumber = 0;

        private ContentDialog? dialog = null;
        private ScrollViewer? contentWrapper = null;

        private HttpClient httpClient = new();
        private HtmlFishBaseSpeciesMetadata? fishBaseSpeciesMetadata = null;
        private bool? pageDownloadAttempted = null;

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


        /// <summary>
        /// The text in the new species FishBase URL textbox has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewSpeciesFishbaseURL_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = NewSpeciesFishBaseURL.Text;
            if (IsValidUrl(url))
                NewSpeciesFishBaseURLOKButton.IsEnabled = true;
            else
                NewSpeciesFishBaseURLOKButton.IsEnabled = false;
        }


        /// <summary>
        /// In the New species dialog this button is used to populate the family/genus/species
        /// Textboxes from the information on a FishBase species summary page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void NewSpeciesFishBaseURLOKButton_Click(object sender, RoutedEventArgs e)
        {
            string url = NewSpeciesFishBaseURL.Text;

            try
            {
                // Disable the download button so it can't be pressed twice
                NewSpeciesFishBaseURLOKButton.IsEnabled = false;

                // Download the page and extract the metadata
                fishBaseSpeciesMetadata = await DownloadFishBaseSummaryPage(url);
                pageDownloadAttempted = true;

                if (fishBaseSpeciesMetadata is not null)
                {
                    SetFishBaseURLValid(true/*trueTickFalseCrossNullNothing*/);

                    // Load the family, genus, species & fish ID
                    SpeciesLatin.Text = fishBaseSpeciesMetadata.SpeciesLatin ?? "";
                    SpeciesCommon.Text = fishBaseSpeciesMetadata.SpeciesCommon ?? "";
                    GenusLatin.Text = fishBaseSpeciesMetadata.Genus ?? "";
                    if (fishBaseSpeciesMetadata.FamilyLatin is not null && fishBaseSpeciesMetadata.FamilyCommon is not null)
                        Family.Text = $"{fishBaseSpeciesMetadata.FamilyLatin}/{fishBaseSpeciesMetadata.FamilyCommon}";
                    else if (fishBaseSpeciesMetadata.FamilyLatin is not null)
                        Family.Text = $"{fishBaseSpeciesMetadata.FamilyLatin}";
                    else
                        Family.Text = string.Empty;

                    if (fishBaseSpeciesMetadata.FishID is not null)
                    {
                        int fishID = (int)fishBaseSpeciesMetadata.FishID;
                        SpeciesCode.Text = SpeciesItem.MakeCodeFromCodeTypeAndCode(CodeType.FishBase, fishID.ToString());
                    }
                    else
                    {
                        SpeciesCode.Text = string.Empty;
                    }
                }
                await ManageControlVisability();
            }
            finally
            {
                // Reenable download button
                NewSpeciesFishBaseURLOKButton.IsEnabled = true;
            }
        }


        private async void SpeciesLatin_TextChanged(object sender, TextChangedEventArgs e)
        {
            EnforceLowercaseTextBox((TextBox)sender);

            await ManageControlVisability();
        }

        private async void SpeciesCommon_TextChanged(object sender, TextChangedEventArgs e)
        {
            await ManageControlVisability();
        }

        private async void GenusLatin_TextChanged(object sender, TextChangedEventArgs e)
        {
            await ManageControlVisability();
        }

        private async void FamilyAutoSuggest_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            await ManageControlVisability();
        }

        private async void FamilyAutoSuggest_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            await ManageControlVisability();
        }

        private async void FamilyAutoSuggest_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            await ManageControlVisability();
        }

        /// <summary>
        /// User press the 'Go To FishBase' button.
        /// Try to go to the summury page for this species
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EditSpeciesFishbaseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            bool ret;
            string genus = GenusLatin.Text.Trim();
            string species = SpeciesLatin.Text.Trim();

            FishBaseID.Text = "";

            try
            {
                // Disable the download button so it can't be pressed twice
                EditSpeciesFishbaseSearchButton.IsEnabled = false;

                ret = await OpenFishBasePageAsync(genus, species);
                if (ret == true)
                {
                    atStepNumber = 2;                
                }
                await ManageControlVisability();
            }
            finally
            {
                // Reenable download button
                EditSpeciesFishbaseSearchButton.IsEnabled = true;
            }

        }



        /// <summary>
        /// The text in the edit species FishBase URL textbox has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditSpeciesFishbaseURL_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = EditSpeciesFishBaseURL.Text;
            if (IsValidUrl(url))
                EditSpeciesFishBaseURLOKButton.IsEnabled = true;
            else
                EditSpeciesFishBaseURLOKButton.IsEnabled = false;
        }


        /// <summary>
        /// Download the provided URL. We'll assume is it a species summary page 
        /// and try to extract the FishBase ID (known as speccode in the html) and
        /// the family,genus and species names from the page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EditSpeciesFishbaseURLDownloadButton_Click(object sender, RoutedEventArgs e)
        {            
            string url = EditSpeciesFishBaseURL.Text;

            try
            {
                // Disable the download button so it can't be pressed twice
                EditSpeciesFishBaseURLOKButton.IsEnabled = false;

                // Download the page and extract the metadata
                fishBaseSpeciesMetadata = await DownloadFishBaseSummaryPage(url);
                pageDownloadAttempted = true;

                if (fishBaseSpeciesMetadata is not null)
                {
                    GenusSpeciesConfirmText.Text = $"Family: {fishBaseSpeciesMetadata.FamilyLatin}\nGenus: {fishBaseSpeciesMetadata.Genus}\nSpecies: {fishBaseSpeciesMetadata.SpeciesLatin}";

                    SetFishBaseURLValid(true/*trueTickFalseCrossNullNothing*/);

                    atStepNumber = 3;
                }
                await ManageControlVisability();
            }
            finally
            {
                // Reenable download button
                EditSpeciesFishBaseURLOKButton.IsEnabled = true;
            }
        }


        /// <summary>
        /// User confirmed the family/genus/species extracted from FishBase.org matches
        /// the species code list record
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void EditSpeciesGenusSpeciesConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (fishBaseSpeciesMetadata is not null && fishBaseSpeciesMetadata.FishID is not null)
            {
                int fishID = (int)fishBaseSpeciesMetadata.FishID;

                speciesItem.Code = SpeciesItem.MakeCodeFromCodeTypeAndCode(CodeType.FishBase, fishID.ToString());
                SpeciesCode.Text = speciesItem.Code;
                FishBaseID.Text = $" {fishBaseSpeciesMetadata.FishID}";

                atStepNumber = 4;                
            }
            await ManageControlVisability();
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
                speciesItem = _speciesItem;

                ClearControls();

                if ((bool)editExisting)
                {
                    title = "Edit Species Record";
                    primaryButtonText = "Update";
                  
                    // Populate the controls with the existing species info
                    Family.Text = speciesItem.Family;
                    GenusLatin.Text = speciesItem.Genus;
                    SpeciesLatin.Text = speciesItem.SpeciesScientific;
                    SpeciesCommon.Text = speciesItem.SpeciesCommon;
                    if (string.Compare(speciesItem.Code, "FishBase:", true/*ignorecase*/) != 0)
                        SpeciesCode.Text = speciesItem.Code;
                    else
                        SpeciesCode.Text = string.Empty;
                    EditSpeciesFishBaseURL.Text = string.Empty;
                    FishBaseID.Text = string.Empty;
                    pageDownloadAttempted = null;

                    //if (IsFishBaseIDSetup(speciesItem.Code))
                    //{
                    //    atStepNumber = 4;
                    //}
                    //else
                    //{
                        atStepNumber = 1;
                    //}
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
                    EditSpeciesFishBaseURL.Text = string.Empty;
                    FishBaseID.Text = string.Empty;
                    pageDownloadAttempted = null;

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
                contentWrapper = new ScrollViewer
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
                await NetworkHelper.IsInternetAvailableHttpAsync(true/*force the underlying check*/);
                await ManageControlVisability();

                // Show the dialog and handle the response
                var result = await dialog.ShowAsync();
                dialog.Content = null;  // Detach the content after the dialog is closed

                // Check if the Add button pressed
                if (result == ContentDialogResult.Primary)
                {
                    // Transfer values form the UI controls
                    speciesItem.Family = Family.Text;
                    speciesItem.Genus = GenusLatin.Text;
                    if (!string.IsNullOrWhiteSpace(SpeciesLatin.Text) && !string.IsNullOrWhiteSpace(SpeciesCommon.Text))
                    {
                        speciesItem.Species = $"{SpeciesLatin.Text}/{SpeciesCommon.Text}";
                    }
                    else if (!string.IsNullOrWhiteSpace(SpeciesLatin.Text))
                    {
                        speciesItem.Species = SpeciesLatin.Text;
                    }
                    else
                        speciesItem.Species = string.Empty;
                    speciesItem.Code = SpeciesCode.Text;

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


        /// <summary>
        /// Clear UI COntrols
        /// </summary>
        private void ClearControls()
        {
            fishBaseSpeciesMetadata = null;
        }


        /// <summary>
        /// Manage the states of the UIelements
        /// </summary>
        private async Task ManageControlVisability()
        {
            if (dialog is not null && editExisting is not null)
            {
                bool isInternetAvailable = await NetworkHelper.IsInternetAvailableHttpAsync();

                if ((bool)editExisting)
                {
                    // Edit existing species record
                    EditExistingLinkToFishBaseGrid.Visibility = Visibility.Visible;

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
                        EditSpeciesFishIDSetupOK.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        EditSpeciesFishIDSetupOK.Visibility = Visibility.Collapsed;
                    }

                    // Internet available?
                    if (isInternetAvailable)
                    {
                        // We have internet
                        EditSpeciesFishIDNoInternet.Visibility = Visibility.Collapsed;
                        EditSpeciesFishbaseSearchButton.IsEnabled = true;

                    }
                    else
                    {
                        // No internet available to ask user to return to this dialog later
                        EditSpeciesFishIDNoInternet.Visibility = Visibility.Visible;
                        EditSpeciesFishbaseSearchButton.IsEnabled = false;
                    }


                    // Show the steps to connect this species code list record to FishBase
                    if (atStepNumber == 0)
                    {
                        // Nothing - no step shown
                        EditSpeciesFishIDInstructionHeader.Visibility = Visibility.Collapsed;
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Collapsed, 3/*fromRowIndex*/, 10/*toRowIndex*/);
                    }
                    else if (atStepNumber == 1)
                    {
                        // Step 1 - Try to open FishBase summary page
                        EditSpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Visible, 3/*fromRowIndex*/, 4/*toRowIndex*/);
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Collapsed, 5/*fromRowIndex*/, 10/*toRowIndex*/);

                        // Scroll to bottom: offsetX=null, offsetY=max vertical offset, zoomFactor=null
                        contentWrapper?.ChangeView(null, contentWrapper.ScrollableHeight, null);
                    }
                    else if (atStepNumber == 2)
                    {
                        // Step 2 - Paste in the URL of the correct FishBase summary page. User press a button to download
                        // the page and the software extracts key items of data including the FishBase ID
                        EditSpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Visible, 3/*fromRowIndex*/, 6/*toRowIndex*/);
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Collapsed, 7/*fromRowIndex*/, 10/*toRowIndex*/);

                        // Scroll to bottom: offsetX=null, offsetY=max vertical offset, zoomFactor=null
                        contentWrapper?.ChangeView(null, contentWrapper.ScrollableHeight, null);
                    }
                    else if (atStepNumber == 3)
                    {
                        // Step 3 - User confirms the Fish's Species/Genus/Family from the FishBase summary page
                        // matched the species we are setting up
                        EditSpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Visible, 3/*fromRowIndex*/, 9/*toRowIndex*/);
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Collapsed, 10/*fromRowIndex*/, 10/*toRowIndex*/);

                        // Scroll to bottom: offsetX=null, offsetY=max vertical offset, zoomFactor=null
                        contentWrapper?.ChangeView(null, contentWrapper.ScrollableHeight, null);
                    }
                    else if (atStepNumber == 4)
                    {
                        // Step 4 - The acquired FishBase fishID is deplayed
                        EditSpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                        GridRowsVisibility(EditExistingLinkToFishBaseGrid, Visibility.Visible, 3/*fromRowIndex*/, 10/*toRowIndex*/);

                        // Scroll to bottom: offsetX=null, offsetY=max vertical offset, zoomFactor=null
                        contentWrapper?.ChangeView(null, contentWrapper.ScrollableHeight, null);
                    }

                    // Enable/Disable the FishBase URL download button
                    string url = EditSpeciesFishBaseURL.Text;
                    if (IsValidUrl(url) && isInternetAvailable)
                        EditSpeciesFishBaseURLOKButton.IsEnabled = true;
                    else
                        EditSpeciesFishBaseURLOKButton.IsEnabled = false;

                    // Set the success/failed indicator next to the FishBase URL download button
                    if (pageDownloadAttempted is null)
                        SetFishBaseURLValid(null);
                    else
                        SetFishBaseURLValid(fishBaseSpeciesMetadata != null);


                    // Hide the 'New SPecies Record' controls
                    EditNewLinkToFishBaseGrid.Visibility = Visibility.Collapsed;
                }
                else 
                {
                    // New Species Record
                    EditNewLinkToFishBaseGrid.Visibility = Visibility.Visible;

                    // Check if enough data is entered to enable the Add or Update button
                    if (!string.IsNullOrEmpty(Family.Text) &&
                        !string.IsNullOrEmpty(GenusLatin.Text) &&
                        !string.IsNullOrEmpty(SpeciesLatin.Text))
                    {
                        dialog.IsPrimaryButtonEnabled = true;
                    }
                    else
                    {
                        dialog.IsPrimaryButtonEnabled = false;
                    }

                    if (isInternetAvailable)
                    {
                        // We have internet
                        NewSpeciesFishIDNoInternet.Visibility = Visibility.Collapsed;
                        NewSpeciesFishBaseURL.IsEnabled = true;
                    }
                    else
                    {
                        // No internet available to ask user to return to this dialog later
                        NewSpeciesFishIDNoInternet.Visibility = Visibility.Visible;
                        NewSpeciesFishBaseURL.IsEnabled = false;
                    }


                    // Set the success/failed indicator next to the FishBase URL download button
                    if (pageDownloadAttempted is null)
                        SetFishBaseURLValid(null);
                    else
                        SetFishBaseURLValid(fishBaseSpeciesMetadata != null);

                    NewSpeciesFishIDInstructionHeader.Visibility = Visibility.Visible;
                    GridRowsVisibility(EditNewLinkToFishBaseGrid, Visibility.Visible, 2/*fromRowIndex*/, 3/*toRowIndex*/);

                    // Hide to controls used to update an existing record
                    EditExistingLinkToFishBaseGrid.Visibility = Visibility.Collapsed;
                }
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
            FontIcon fontIcon;

            if (editExisting is not null)
            {
                if ((bool)editExisting)
                {
                    fontIcon = EditSpeciesFishBaseURLValid;
                }
                else
                {
                    fontIcon = NewSpeciesFishBaseURLValid;
                }

                if (trueTickFalseCrossNullNothing is null)
                {
                    fontIcon.Glyph = string.Empty;
                }
                else if ((bool)trueTickFalseCrossNullNothing)
                {
                    // Tick
                    fontIcon.Glyph = "\uE73E"; // glyph 'Check'
                    fontIcon.Foreground = new SolidColorBrush(Colors.Green);
                    fontIcon.Visibility = Visibility.Visible;
                }
                else
                {
                    // Cross
                    fontIcon.Glyph = "\uE711"; // glyph 'Close'
                    fontIcon.Foreground = new SolidColorBrush(Colors.Red);
                    fontIcon.Visibility = Visibility.Visible;
                }
            }
        }


        /// <summary>
        /// Download the URL which needs to be a FishBase summary page and
        /// extract the Family latin and common names, the genus, the species laton
        /// and common names, the unique FishBase ID, then environment text, distribution text
        /// and the species size text
        /// </summary>
        /// <param name="url"></param>
        /// <returns>FishBaseSpeciesMetadata or null is fails</returns>
        private async Task<HtmlFishBaseSpeciesMetadata?> DownloadFishBaseSummaryPage(string url)
        {
            HtmlFishBaseSpeciesMetadata? fishBaseSpeciesMetadata = null;

            string localFileSpec = url.GetHashCode().ToString("X") + "M.html";

            try
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();

                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(localFileSpec, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(file, content);
                }
                catch (HttpRequestException ex)
                {
                    string reportText = $"Failed to download FishBase page {url}, {ex.Message}";
                    report?.Warning("", reportText);
                    Debug.WriteLine($"SpeciesRecordEditDialog.DownloadFishBaseSummaryPage {reportText}");
                }

                try
                {
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(localFileSpec);
                    fishBaseSpeciesMetadata = await ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(file.Path);

                    if (fishBaseSpeciesMetadata is not null)
                    {
                        if (fishBaseSpeciesMetadata.FamilyLatin is null ||
                            fishBaseSpeciesMetadata.Genus is null ||
                            fishBaseSpeciesMetadata.SpeciesLatin is null ||
                            fishBaseSpeciesMetadata.FishID is null)
                        {
                            // Must have a FishID to be valid
                            fishBaseSpeciesMetadata = null;
                        }
                    }

                    // Clean up
                    await file.DeleteAsync();
                }
                catch (Exception ex)
                {
                    string reportText = $"Failed to extract Fish ID fron the FishBase page {url}, {ex.Message}";
                    report?.Warning("", reportText);
                    Debug.WriteLine($"SpeciesRecordEditDialog.DownloadFishBaseSummaryPage {reportText}");
                }
            }
            catch (Exception ex)
            {
                string reportText = $"Failed to get FishBase page {url}, {ex.Message}";
                report?.Warning("", reportText);
                Debug.WriteLine($"SpeciesRecordEditDialog.DownloadFishBaseSummaryPage {reportText}");
            }

            return fishBaseSpeciesMetadata;
        }

    }
}
