// SpeciesSelector  Used to assigned a species 
//
// Version 1.0 
//
// Version 1.1 18 Apr 2025
// Added support for Fishbase fish images

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.SpeciesImageCache;


namespace Surveyor.User_Controls
{
    public class ImageDataObject
    {
        public Uri? ImageLocation { get; set; } = null;
        public string Author { get; set; } = "";
    }

    public sealed partial class SpeciesSelector : UserControl
    {
        private string? fileSpecSpecies;
        private Reporter? report;
        private SpeciesImageCache? speciesImageCache = null;
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
            DumpClassPropertiesHelper.DumpAllProperties(this);
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

            speciesCodeList.Load(fileSpec, trueScientificFalseCommonName);
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
        internal async Task<bool> SpeciesNew(MainWindow mainWindow, SpeciesInfo speciesInfo, SpeciesImageCache speciesImageCache)
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
        internal async Task<bool> SpeciesEdit(MainWindow mainWindow, SpeciesInfo speciesInfo, SpeciesImageCache speciesImageCache)
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
            SourceGenusSpecies.Text = string.Empty;

            // Clear ItemsSource bindings
            AutoSuggestSpecies.ItemsSource = null;
            AutoSuggestGenus.ItemsSource = null;
            AutoSuggestFamily.ItemsSource = null;

            // Clear image list
            ImageList.Clear();
        }


        /// <summary>
        /// Handle the edit or new (create) species info dialog
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <param name="editExisting"></param>
        /// <returns>true is the speciesInfo parameter has been changed</returns>
        private async Task<bool> SpeciesEditorNew(MainWindow mainWindow, SpeciesInfo speciesInfo, bool editExisting, SpeciesImageCache _speciesImageCache)
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
           

            // If Edit mode, fill in the fields
            if (editExisting)
            {
                AutoSuggestSpecies.Text = speciesInfo.Species;
                AutoSuggestGenus.Text = speciesInfo.Genus;
                AutoSuggestFamily.Text = speciesInfo.Family;
                NumberBoxNumberOfFish.Value = Convert.ToInt32(speciesInfo.Number);
                //???ComboBoxLifeStage.SelectedItem = speciesInfo.Stage;
                TextBoxComment.Text = speciesInfo.Comment;
            }


            // Hook up closed cleanup handler
            dialog.Closed += (s, e) =>
            {
                ClearControls();
            };


            // Show the dialog and handle the response
            var result = await dialog.ShowAsync();
            dialog.Content = null;  // Detach the content after the dialog is closed

            // Check if the Add button pressed
            if (result == ContentDialogResult.Primary)
            {
                // Check for selected Species
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

                userSelectedGenus = false;
                userSelectedFamily = false;

                // Do a fuzzy search based on the text
                if (speciesCodeList.SearchSpecies(args.QueryText, genus, family) == true)
                {
                    AutoSuggestSpecies.ItemsSource = speciesCodeList.SpeciesComboItems;

                    // If the search resulted in one result then use that result as the selection
                    if (speciesCodeList.SpeciesComboItems.Count == 1)
                    {
                        SpeciesItem speciesItem = speciesCodeList.SpeciesComboItems[0];
                        await SpeciesSelected(speciesItem, true/*setAutoSuggesat*/);
                    }
                }
            }
        }

        private async void AutoSuggestBoxSpecies_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            SpeciesItem speciesItem = (SpeciesItem)args.SelectedItem;

            await SpeciesSelected(speciesItem, false/*setAutoSuggesat*/);
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

            userSelectedGenus = false;
            userSelectedFamily = false;

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
                userSelectedFamily = false;

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
                userSelectedFamily = false;

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
            userSelectedFamily = false;
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
                userSelectedGenus = false;
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
                userSelectedGenus = false;
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

            try
            {
                // Populate image
                string source = "";
                string genusSpecies = "";

                List<SpeciesImageItem>? SpeciesImageItemList = speciesImageCache?.GetImagesForSpecies(speciesItem.Code);

                if (SpeciesImageItemList is not null && SpeciesImageItemList.Count > 0)
                {
                    if (speciesItem is not null)
                    {
                        SpeciesItem.CodeType codeType = speciesItem.ExtractCodeType();
                        switch (codeType)
                        {
                            case SpeciesItem.CodeType.FishBase:
                                source = "Fishbase";
                                break;
                            default:
                                source = "Unknown";
                                break;
                        }
                        genusSpecies = speciesItem.Genus + " - " + speciesItem.Species;

                        SourceGenusSpecies.Text = $"Source: {source} {genusSpecies}";

                        Debug.WriteLine($"SpeciesSelector: AutoSuggestBoxSpecies_SuggestionChosen: {genusSpecies} {source} {speciesItem.Code}");
                    }

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
                }
                else
                {
                    SourceGenusSpecies.Text = string.Empty;

                    // Clear image list
                    ImageList.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SpeciesSelector: DisplayCachedFishImages: {ex.Message}");
                report?.Error("SpeciesSelector", $"DisplayCachedFishImages: {ex.Message}");
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
