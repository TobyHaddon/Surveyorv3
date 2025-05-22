// SpeciesSelector  Used to assigned a species 
//
// Version 1.0 
//
// Version 1.1 18 Apr 2025
// Added support for Fishbase fish images

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.SpeciesImageAndInfoCache;


namespace Surveyor.User_Controls
{
    public class ImageDataObject
    {
        public Uri? ImageLocation { get; set; } = null;
        public string Author { get; set; } = string.Empty;
    }

    public sealed partial class SpeciesSelector : UserControl
    {
        private string? fileSpecSpecies;
        private Reporter? report;
        private SpeciesImageAndInfoCache? speciesImageCache = null;
        internal SpeciesCodeList speciesCodeList { get; } = new() ;

        private bool userSelectedGenus = false; // Set to true if the user selected a genus from the AutoSuggestBox or typed in a name as opposed to it being calculated from the species
        private bool userSelectedFamily = false; // Set to true if the user selected a family from the AutoSuggestBox or typed in a name as opposed to it being calculated from the species or genus


        // Updatable image list to hold the fish images. Dynamically bound to the GridView
        public ObservableCollection<ImageDataObject> ImageList { get; set; } = [];



        public SpeciesSelector()
        {
            this.InitializeComponent();            
        }


        /// <summary>
        /// Diags dump of class information
        /// </summary>
        public void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report);
        }

        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter _report)
        {
            report = _report;
        }


        /// <summary>
        /// Called to load the species code list
        /// </summary>
        /// <param name="fileSpec"></param>
        public void Load(string fileSpec, bool trueScientificFalseCommonName)
        {
            fileSpecSpecies = fileSpec;

            speciesCodeList.Load(fileSpec, report, trueScientificFalseCommonName);
        }


        /// <summary>
        /// Called to unload the species code list
        /// </summary>
        public void Unload()
        {
            speciesCodeList.Unload();
            fileSpecSpecies = null;
        }


        /// <summary>
        /// Create a new species record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        internal async Task<bool> SpeciesNew(MainWindow mainWindow, SpeciesInfo speciesInfo, SpeciesImageAndInfoCache speciesImageCache)
        {
            // Clear the speciesInfo instance as this is a New species assignment
            speciesInfo.Clear();

            return await SpeciesEditorNew(mainWindow, speciesInfo, false/*removeButton*/, speciesImageCache);
        }


        /// <summary>
        /// Exist an existing species info record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        internal async Task<bool> SpeciesEdit(MainWindow mainWindow, SpeciesInfo speciesInfo, SpeciesImageAndInfoCache speciesImageCache)
        {
            return await SpeciesEditorNew(mainWindow, speciesInfo, true/*removeButton*/, speciesImageCache);
        }


        ///
        /// PRIVATE
        ///

        /// <summary>
        /// Clear the UI controls
        /// </summary>
        private void ClearControls()
        {
            // Clear control values
            AutoSuggestSpecies.Text = string.Empty;
            AutoSuggestGenus.Text = string.Empty;
            AutoSuggestFamily.Text = string.Empty;
            TextBoxComment.Text = string.Empty;
            NumberBoxNumberOfFish.Value = 1;


            // Clear ItemsSource bindings
            AutoSuggestSpecies.ItemsSource = null;
            AutoSuggestGenus.ItemsSource = null;
            AutoSuggestFamily.ItemsSource = null;

            // Clear image list (bound to xaml)
            ImageList.Clear();

            // Clear source credit and genus/species  (bound to xaml)
            SourceCredit.Text = string.Empty;
            GenusSpecies.Text = string.Empty;

            // Clear environment, distrution and size (bound to xaml)            
            Environment.Text = string.Empty;
            Distribution.Text = string.Empty;
            SpeciesSize.Text = string.Empty;

        }


        /// <summary>
        /// Handle the edit or new (create) species info dialog
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="editExisting"></param>
        /// <returns>true is the speciesInfo parameter has been changed</returns>
        private async Task<bool> SpeciesEditorNew(MainWindow mainWindow, SpeciesInfo speciesInfo, bool editExisting, SpeciesImageAndInfoCache _speciesImageCache)
        {
            bool ret = false;

            speciesImageCache = _speciesImageCache;

            ClearControls();

            userSelectedGenus = false;
            userSelectedFamily = false;


            // Create the dialog
            ContentDialog dialog = new()
            {
                Content = this,
                Title = "Assign Species",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Add",
                MaxWidth = 800, // <-- important!
                MinWidth = 400,
                XamlRoot = mainWindow.Content.XamlRoot  // Set the XamlRoot property
            };

            if (editExisting)
            {
                dialog.PrimaryButtonText = "Apply";
                dialog.SecondaryButtonText = "Remove";
            }

            // Setup an open dialog handler
            dialog.Opened += Dialog_Opened;

            // Set which class property from ComboItem to use for the display text
            // in the auto suggest boxes
            AutoSuggestSpecies.TextMemberPath = "Species";
            AutoSuggestGenus.TextMemberPath = "Genus";
            AutoSuggestFamily.TextMemberPath = "Family";
            AutoSuggestSpecies.DisplayMemberPath = "Species";
            AutoSuggestGenus.DisplayMemberPath = "Genus";
            AutoSuggestFamily.DisplayMemberPath = "Family";


            // Load the Life Stage Combo AD - Adult , F - Female, J - Juvenal , M - Male
            // ???NOT NEEDED BY OPWALL - REMOVE
            //???List<string> lifeStages = new()
            //{
            //    "Adult",
            //    "Juvenile",
            //    "Female",
            //    "Male"
            //};
            //ComboBoxLifeStage.ItemsSource = lifeStages;

            // Hide the species info expander
            SpeciesInfoExpander.Visibility = Visibility.Collapsed;

            // If Edit mode, fill in the fields
            if (editExisting)
            {
                // First set the species and select the equivalent item in the pick list 
                // This is so the images are displayed (if available)
                if (speciesInfo.Species is not null)
                {
                    AutoSuggestSpecies.Text = speciesInfo.Species;
                    if (speciesCodeList.SearchSpecies(speciesInfo.Species, ""/*genus*/, ""/*family*/) == true)
                    {
                        AutoSuggestSpecies.ItemsSource = speciesCodeList.SpeciesComboItems;

                        // If the search resulted in one result then use that result as the selection
                        if (speciesCodeList.SpeciesComboItems.Count == 1)
                        {
                            SpeciesItem speciesItem = speciesCodeList.SpeciesComboItems[0];
                            await SpeciesSelected(speciesItem, true/*setAutoSuggest*/);
                        }
                    }
                }

                // Set the genus, family, fish count and comment
                AutoSuggestGenus.Text = speciesInfo.Genus;
                AutoSuggestFamily.Text = speciesInfo.Family;
                NumberBoxNumberOfFish.Value = Convert.ToInt32(speciesInfo.Number);
                //???ComboBoxLifeStage.SelectedItem = speciesInfo.Stage;
                TextBoxComment.Text = speciesInfo.Comment;
            }


            // Show the dialog and handle the response
            var result = await dialog.ShowAsync();

            // Check if the Add button pressed
            if (result == ContentDialogResult.Primary)
            {
                // Check for selected Species from the text
                string? species = AutoSuggestSpecies.Text;

                if (species is not null)
                {
                    SpeciesItem? speciesItemLookup = speciesCodeList.GetSpeciesItemBySpeciesName(species);

                    if (speciesItemLookup is not null)
                    {
                        // Check if Species value has changed
                        if (speciesInfo.Species != speciesItemLookup.Species)
                        {
                            speciesInfo.Species = speciesItemLookup.Species;
                            ret = true;
                        }
                        // Check if the Genus value has changed
                        if (speciesInfo.Genus != speciesItemLookup.Genus)
                        {
                            speciesInfo.Genus = speciesItemLookup.Genus;
                            ret = true;
                        }
                        // Check if the Family value has changed
                        if (speciesInfo.Family != speciesItemLookup.Family)
                        {
                            speciesInfo.Family = speciesItemLookup.Family;
                            ret = true;
                        }
                        // Check if the Code value has changed
                        if (speciesInfo.Code != speciesItemLookup.Code)
                        {
                            speciesInfo.Code = speciesItemLookup.Code;
                            ret = true;
                        }
                    }
                    else
                    {
                        // Check if the Species value has changed
                        if (speciesInfo.Species != species)
                        {
                            speciesInfo.Species = species;
                            ret = true;
                        }
                        // Check if the Genus value has changed
                        if (speciesInfo.Genus != AutoSuggestGenus.Text)
                        {
                            speciesInfo.Genus = AutoSuggestGenus.Text;
                            ret = true;
                        }
                        // Check if the Family value has changed
                        if (speciesInfo.Family != AutoSuggestFamily.Text)
                        {
                            speciesInfo.Family = AutoSuggestFamily.Text;
                            ret = true;
                        }
                    }
                }

                // Number of fish
                if (speciesInfo.Number != NumberBoxNumberOfFish.Value.ToString())
                {
                    speciesInfo.Number = NumberBoxNumberOfFish.Value.ToString();
                    ret = true;
                }

                // Life stage of the fish
                //???Not used by Opwall
                //???if (speciesInfo.Stage != ComboBoxLifeStage.SelectedItem as string)
                //{
                //    speciesInfo.Stage = ComboBoxLifeStage.SelectedItem as string;
                //    ret = true;
                //}

                // Activity of the fish (not used yet)

                // Comment
                if (speciesInfo.Comment != TextBoxComment.Text)
                {
                    speciesInfo.Comment = TextBoxComment.Text;
                    ret = true;
                }
            }
            // Check if the Remove button pressed 
            else if (result == ContentDialogResult.Secondary)
            {
                speciesInfo.Clear();
                ret = true;
            }

            ClearControls();
            dialog.Content = null;  // Detach the content after the dialog is closed

            return ret;
        }


        ///
        /// Events
        /// 


        /// <summary>
        /// Event handler called when the dialog is opened
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            // Handle the dialog being opened
            EnableButtons();
        }


        /// <summary>
        /// Event handler called if the user edits the species auto suggest box text
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void AutoSuggestBoxSpecies_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Since selecting an item will also change the text,
            // only listen to changes caused by user entering text.
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string genus = AutoSuggestGenus.Text;
                string family = AutoSuggestFamily.Text;

                userSelectedGenus = false;
                userSelectedFamily = false;

                // Search on the genus
                if (speciesCodeList.SearchSpecies(sender.Text, genus, family) == true)
                {
                    AutoSuggestSpecies.ItemsSource = speciesCodeList.SpeciesComboItems;
                    Debug.WriteLine($"AutoSuggestBoxSpecies_TextChanged: speciesCodeList.SpeciesComboItems = {speciesCodeList.SpeciesComboItems.Count}");
                }
                else
                    Debug.WriteLine($"AutoSuggestBoxSpecies_TextChanged: speciesCodeList.SearchSpecies failed");
            }
        }

        private async void AutoSuggestBoxSpecies_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null && args.ChosenSuggestion is SpeciesItem item)
            {
                // User pressed Enter after selecting a suggestion from the results list
            }
            else if (!string.IsNullOrEmpty(args.QueryText) || sender.Text == "")
            {
                // User typed text and pressed Enter without selecting a suggestion
                string genus = AutoSuggestGenus.Text;
                string family = AutoSuggestFamily.Text;

                // Do a fuzzy search based on the text
                // If the actually selected or typed genus or family values then use
                // those in the search as well (that is as opposed to the values being
                // calculated from the species)
                string genusSearch  = userSelectedGenus == true ? genus : string.Empty;
                string familySearch = userSelectedFamily == true ? family : string.Empty;

                if (speciesCodeList.SearchSpecies(args.QueryText, genusSearch, familySearch) == true)
                {
                    AutoSuggestSpecies.ItemsSource = speciesCodeList.SpeciesComboItems;

                    // If the search resulted in one result then use that result as the selection
                    if (speciesCodeList.SpeciesComboItems.Count == 1)
                    {
                        SpeciesItem speciesItem = speciesCodeList.SpeciesComboItems[0];
                        await SpeciesSelected(speciesItem, true/*setAutoSuggest*/);
                    }
                }
            }
        }

        private async void AutoSuggestBoxSpecies_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            SpeciesItem speciesItem = (SpeciesItem)args.SelectedItem;

            await SpeciesSelected(speciesItem, true/*setAutoSuggesat*/);
        }


        /// <summary>
        /// Set the selected species item, displaying it's genus and family and fish images from the cache
        /// </summary>
        /// <param name="speciesItem"></param>
        private async Task SpeciesSelected(SpeciesItem speciesItem, bool setAutoSuggestText = false)
        {
            WinUIGuards.CheckIsUIThread();

            // Set the genus and family
            if (setAutoSuggestText)
            {
                AutoSuggestGenus.Text = speciesItem.Genus;
                AutoSuggestFamily.Text = speciesItem.Family;
            }

            // Display images of fish for this species to help fish ID
            await DisplayCachedFishImages(speciesItem);
        }


        /// <summary>
        /// Event handler called if the user edits the genus auto suggest box text
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void AutoSuggestBoxGenus_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Since selecting an item will also change the text,
            // only listen to changes caused by user entering text.
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string family = AutoSuggestFamily.Text;

                userSelectedGenus = true;

                // Search on the genus
                if (speciesCodeList.SearchGenus(sender.Text, family) == true)
                {
                    AutoSuggestGenus.ItemsSource = speciesCodeList.GenusComboItems;
                    Debug.WriteLine($"AutoSuggestBoxGenus_TextChanged: speciesCodeList.SpeciesComboItems = {speciesCodeList.GenusComboItems.Count}");
                }
                else
                    Debug.WriteLine($"AutoSuggestBoxGenus_TextChanged: speciesCodeList.SearchSpecies failed");
            }
        }

        private void AutoSuggestBoxGenus_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null && args.ChosenSuggestion is SpeciesItem item)
            {
                // User pressed Enter after selecting a suggestion from the results list
                //                if (item != null)
                //                // User selected an item, take an action
                //???do we need this?                sender.Text = item.Species;
            }
            else if (!string.IsNullOrEmpty(args.QueryText) || sender.Text == "")
            {
                // User typed text and pressed Enter without selecting a suggestion
                string family = AutoSuggestFamily.Text;

                userSelectedGenus = true;

                // Do a fuzzy search based on the text
                if (speciesCodeList.SearchGenus(sender.Text, family) == true)
                    AutoSuggestGenus.ItemsSource = speciesCodeList.GenusComboItems;
            }
        }

        private void AutoSuggestBoxGenus_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            SpeciesItem item = (SpeciesItem)args.SelectedItem;

            // Set the family
            AutoSuggestFamily.Text = item.Family;

            userSelectedGenus = true;
        }


        /// <summary>
        /// Event handler called if the user edits the family auto suggest box text
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void AutoSuggestBoxFamily_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Since selecting an item will also change the text,
            // only listen to changes caused by user entering text.
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                userSelectedFamily = true;

                // Search on the family
                if (speciesCodeList.SearchFamily(sender.Text) == true)
                {
                    AutoSuggestSpecies.ItemsSource = speciesCodeList.GenusComboItems;
                    Debug.WriteLine($"AutoSuggestBoxFamily_TextChanged: speciesCodeList.SpeciesComboItems = {speciesCodeList.GenusComboItems.Count}");
                }
                else
                    Debug.WriteLine($"AutoSuggestBoxFamily_TextChanged: speciesCodeList.SearchSpecies failed");
            }
        }

        private void AutoSuggestBoxFamily_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null && args.ChosenSuggestion is SpeciesItem item)
            {
                // User pressed Enter after selecting a suggestion from the results list
                //                if (item != null)
                //                // User selected an item, take an action
                //???do we need this?                sender.Text = item.Species;
            }
            else if (!string.IsNullOrEmpty(args.QueryText) || sender.Text == "")
            {
                userSelectedFamily = true;

                // Do a fuzzy search based on the text
                if (speciesCodeList.SearchFamily(sender.Text) == true)
                    AutoSuggestFamily.ItemsSource = speciesCodeList.FamilyComboItems;
            }

        }

        private void AutoSuggestBoxFamily_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            //???sender.Text = args.SelectedItem.ToString();
            //???Debug.WriteLine($"AutoSuggestBoxFamily_SuggestionChosen: {sender.Text}");
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Check if the 'Add' button should be enabled
        /// </summary>
        private void EnableButtons()
        {
            // Primary or the 'Add' button
            var primaryButton = FindName("PrimaryButton") as Button;
            if (primaryButton != null)
            {
                if (string.IsNullOrWhiteSpace(AutoSuggestSpecies.Text))
                    primaryButton.IsEnabled = false;
                else
                    primaryButton.IsEnabled = true;
            }

            // Secondary of the 'Remove' button (only for the Edit dialog)
            var SecondaryButtonText = FindName("SecondaryButton") as Button;
            if (SecondaryButtonText != null)
            {
                SecondaryButtonText.IsEnabled = false;
            }
        }


        /// <summary>
        /// Fill the indicated AUtoSuggestBox with results from the ComboItem list
        /// </summary>
        /// <param name="autoSuggest"></param>
        /// <param name="list"></param>
        private void FillAutoSuggestBox(AutoSuggestBox autoSuggest, List<ComboItem> list)
        {
            var suitableItems = new List<string>();

            suitableItems.Clear();

            // Load the AutoSuggest control with the results
            foreach (var item in list)
            {
                if (item.DisplayText is not null)
                    suitableItems.Add(item.DisplayText);
            }

            if (suitableItems.Count > 0)
                autoSuggest.ItemsSource = suitableItems;
            else
                autoSuggest.ItemsSource = new string[] { "No results found" };
        }


        /// <summary>
        /// Display the cached fish images in a GridView
        /// </summary>
        /// <param name="speciesItem"></param>
        private async Task DisplayCachedFishImages(SpeciesItem speciesItem)
        {
            WinUIGuards.CheckIsUIThread();

            if (speciesImageCache is not null)
            {
                try
                {
                    // Populate image
                    string source = "";
                    string genusSpecies = "";

                    List<SpeciesImageItem>? SpeciesImageItemList = speciesImageCache.GetImagesForSpecies(speciesItem.Code);

                    if (SpeciesImageItemList is not null && SpeciesImageItemList.Count > 0)
                    {
                        if (speciesItem is not null)
                        {
                            // Get the image source for accreditation 
                            SpeciesItem.CodeType codeType = speciesItem.ExtractCodeType();
                            source = codeType switch
                            {
                                SpeciesItem.CodeType.FishBase => "Fishbase",
                                _ => "Unknown",
                            };
                            genusSpecies = speciesItem.Genus + " - " + speciesItem.Species;

                            SourceCredit.Text = $"Source: {source}";
                            GenusSpecies.Text = genusSpecies;
                            SpeciesInfoExpander.Visibility = Visibility.Visible;
                            // The state of a .XAML based dialog is remembered. Therefore you need to close
                            // the expander in case the user left it open last time the Assign Species dialog
                            // box was displayed
                            SpeciesInfoExpander.IsExpanded = false;

                            //???TOBEDELELTED SourceGenusSpecies.Text = $"Source: {source} {genusSpecies}";

                            Debug.WriteLine($"SpeciesSelector: AutoSuggestBoxSpecies_SuggestionChosen: {genusSpecies} {source} {speciesItem.Code}");
                        }

                        // Make the fish image accessble to the XAML put loading into the
                        // Image List
                        ImageList.Clear();
                        foreach (SpeciesImageItem speciesImageItem in SpeciesImageItemList)
                        {
                            var file = await ApplicationData.Current.LocalFolder.GetFileAsync(speciesImageItem.ImageFile);
                            var fileUri = new Uri("file:///" + file.Path.Replace("\\", "/"));

                            ImageList.Add(new ImageDataObject
                            {
                                ImageLocation = fileUri,
                                Author = speciesImageItem.Author
                            });
                        }

                        // Get the species environment, distribution and species size information from the cache
                        (string environment, string distribution, string speciesSize) = speciesImageCache.GetInfo(speciesItem!.Code);
                        Environment.Text = environment;
                        Distribution.Text = distribution;
                        SpeciesSize.Text = speciesSize;

                        // Hide any blank information rows (note there is no Visibility property on a grid row)
                        GridRowsVisibility(SpeciesInfoGrid, string.IsNullOrEmpty(environment) ? Visibility.Collapsed : Visibility.Visible, 0/*fromRowIndex*/, 0/*toRowIndex*/);
                        GridRowsVisibility(SpeciesInfoGrid, string.IsNullOrEmpty(distribution) ? Visibility.Collapsed : Visibility.Visible, 1/*fromRowIndex*/, 1/*toRowIndex*/);
                        GridRowsVisibility(SpeciesInfoGrid, string.IsNullOrEmpty(speciesSize) ? Visibility.Collapsed : Visibility.Visible, 2/*fromRowIndex*/, 2/*toRowIndex*/);
                    }
                    else
                    {
                        // Clear image list (bound to xaml)
                        ImageList.Clear();

                        // Clear source credit and genus/species  (bound to xaml)
                        SourceCredit.Text = string.Empty;
                        GenusSpecies.Text = string.Empty;

                        // Clear environment, distrution and size (bound to xaml)
                        Environment.Text = string.Empty;
                        Distribution.Text = string.Empty;
                        SpeciesSize.Text = string.Empty;
                        SpeciesInfoExpander.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    report?.Error("SpeciesSelector", $"DisplayCachedFishImages: {ex.Message}");
                }
            }
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

        // *** End of SpeciesSelector ***
    }


 

    /// <summary>
    /// Used to convert the image Uri
    /// </summary>
    public class UriToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Uri uri)
            {
                return new BitmapImage(uri);
            }
            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }


    /// <summary>
    /// Add "by " as a prefix to the author is the Author proirity has a value
    /// </summary>
    public class AuthorDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var author = value as string;
            if (string.IsNullOrWhiteSpace(author))
                return string.Empty;

            return $"by {author}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

}
