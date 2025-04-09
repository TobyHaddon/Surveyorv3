//using ExampleMagnifierWinUI;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;


namespace Surveyor.User_Controls
{
    public sealed partial class SpeciesSelector : UserControl
    {
        private string? fileSpecSpecies;

        private SpeciesCodeList speciesCodeList = new();

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
        /// Create a new species record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        public async Task<bool> SpeciesNew(MainWindow mainWindow, SpeciesInfo speciesInfo)
        {
            // Clear the speciesInfo instance as this is a New species assignment
            speciesInfo.Clear();

            return await SpeciesEditorNew(mainWindow, speciesInfo, true/*removeButton*/);
        }


        /// <summary>
        /// Exist an existing species info record
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="speciesInfo"></param>
        /// <returns></returns>
        public async Task<bool> SpeciesEdit(MainWindow mainWindow, SpeciesInfo speciesInfo)
        {
            return await SpeciesEditorNew(mainWindow, speciesInfo, true/*removeButton*/);
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

        private async Task<bool> SpeciesEditorNew(MainWindow mainWindow, SpeciesInfo speciesInfo, bool editExisting)
        {
            bool ret = false;

            // Load the code list
            speciesCodeList.Load("species.txt");

            // Create the dialog
            ContentDialog dialog = new()
            {
                Content = this,
                Title = "Assign Species",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Add",
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
            List<string> lifeStages = new()
            {
                "Adult",
                "Juvenile",
                "Female",
                "Male"
            };
            ComboBoxLifeStage.ItemsSource = lifeStages;
           

            // If Edit mode, fill in the fields
            if (editExisting)
            {
                AutoSuggestSpecies.Text = speciesInfo.Species;
                AutoSuggestGenus.Text = speciesInfo.Genus;
                AutoSuggestFamily.Text = speciesInfo.Family;
                NumberBoxNumberOfFish.Value = Convert.ToInt32(speciesInfo.Number);
                ComboBoxLifeStage.SelectedItem = speciesInfo.Stage;
                TextBoxComment.Text = speciesInfo.Comment;
            }


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
                    SpeciesItem? speciesItemLookup = speciesCodeList.GetSpeciesItem(species);

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
                if (speciesInfo.Stage != ComboBoxLifeStage.SelectedItem as string)
                {
                    speciesInfo.Stage = ComboBoxLifeStage.SelectedItem as string;
                    ret = true;
                }

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


        /// <summary>
        /// Called to load the species code list
        /// </summary>
        /// <param name="fileSpec"></param>
        private void Load(string fileSpec)
        {
            fileSpecSpecies = fileSpec;

            speciesCodeList.Load(fileSpec);
        }


        /// <summary>
        /// Called to unload the species code list
        /// </summary>
        private void Unload()
        {
            speciesCodeList.Unload();
            fileSpecSpecies = null;
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

        private void AutoSuggestBoxSpecies_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
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
                if (speciesCodeList.SearchSpecies(args.QueryText, genus, family) == true)
                    AutoSuggestSpecies.ItemsSource = speciesCodeList.SpeciesComboItems;
            }
        }

        private void AutoSuggestBoxSpecies_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            SpeciesItem item = (SpeciesItem)args.SelectedItem;

            // Set the genus and family
            AutoSuggestGenus.Text = item.Genus;
            AutoSuggestFamily.Text = item.Family;
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
    }
}
