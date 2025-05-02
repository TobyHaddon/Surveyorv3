// Surveyor SpeciesCodeList
// Manages the loaded and the selected species code list
// 
// Version 1.0
//
// Version 1.1 20 Apr 2025
// Added the ability to new, update and delete species items (and write to disk)
//
// Version 1.2 30 Apr 2025
// Moved the species.txt file to be in the local folder (was in same directory as the executable)
// The Load() funcion will copy the file from the executable directory to the local folder if
// it doesn't exist
// Added a GetHash() function 


using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static Surveyor.SpeciesItem;

namespace Surveyor
{
    public partial class SpeciesItem : INotifyPropertyChanged
    {
        // The family of the species format: ScientificName/CommonName
        private string _family = string.Empty;        
        public string Family
        {
            get => _family;
            set
            {
                if (_family != value)
                {
                    _family = value ?? string.Empty;
                    (FamilyScientific, FamilyCommon) = SplitField(_family);
                    UpdateIsScientificAndCommonNamePresent();
                    OnPropertyChanged(nameof(Family));
                }
            }
        }

        // The Genus of the species format: ScientificName
        private string _genus = string.Empty;
        public string Genus
        {
            get => _genus;
            set
            {
                if (_genus != value)
                {
                    _genus = value;
                    OnPropertyChanged(nameof(Genus));
                }
            }
        }

        // The species of the species format: ScientificName/CommonName
        private string _species = string.Empty;
        public string Species
        {
            get => _species;
            set
            {
                if (_species != value)
                {
                    _species = value ?? string.Empty;
                    (SpeciesScientific, SpeciesCommon) = SplitField(_species);
                    UpdateIsScientificAndCommonNamePresent();
                    OnPropertyChanged(nameof(Species));
                }
            }
        }

        // The species code format: CodeType:ID
        private string _code = string.Empty;
        public string Code
        {
            get => _code;
            set
            {
                if (_code != value)
                {
                    _code = value;
                    OnPropertyChanged(nameof(Code));
                }
            }
        }

        // Scientific name for Family, Species
        public string FamilyScientific { get; set; } = "";
        public string SpeciesScientific { get; set; } = "";

        // Common name  for Family, Species
        public string FamilyCommon { get; set; } = "";
        public string SpeciesCommon { get; set; } = "";

        // Indicate if the scientific and common names are present
        public bool IsScientificAndCommonNamePresent { get; set; } = false;

        // Display types
        [Flags]
        public enum DisplayType
        {
            Scientific,
            Common
        }

        // Code tyoe
        public enum CodeType
        {
            None,
            Unknown,
            FishBase
        }


        /// <summary>
        /// Load the species item from a line of text
        /// </summary>
        /// <param name="itemLine"></param>
        /// <returns></returns>
        public bool Load(string itemLine)
        {
            // Split the line into parts using a tab character
            var parts = itemLine.Split('\t');
            if (parts.Length < 3)
                return false;


            Family = parts[0].Trim();
            Genus = parts[1].Trim();
            Species = parts[2].Trim();
            if (parts.Length >= 4)
                Code = parts[3].Trim();

            if (string.Compare(Family, "Family", true) == 0)
                return false; // Skip the header line
          
            return true;
        }


        /// <summary>
        /// Split the field into scientific and common names using '/'
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        private static (string scientific, string common) SplitField(string field)
        {
            var parts = field.Split('/');
            if (parts.Length == 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }

            return ("", "");
        }


        /// <summary>
        /// Maintain the check value for the IsScientificAndCommonNamePresent feild
        /// </summary>
        private void UpdateIsScientificAndCommonNamePresent()
        {
            IsScientificAndCommonNamePresent =
                (!string.IsNullOrWhiteSpace(FamilyScientific) && !string.IsNullOrWhiteSpace(FamilyCommon)) ||
                (!string.IsNullOrWhiteSpace(SpeciesScientific) && !string.IsNullOrWhiteSpace(SpeciesCommon));
        }


        /// <summary>
        /// Splits the SpeciesItem.Code into the CodeType and the Code
        /// e.g. FishBase:3452 is CodeType.FishBase Code = 3452
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public (CodeType codeType, string ID) ExtractCodeTypeAndID()
        {
            if (string.IsNullOrWhiteSpace(this.Code))
                return (CodeType.None, string.Empty);

            var parts = this.Code.Split(':', 2);
            if (parts.Length == 2)
            {
                if (Enum.TryParse(parts[0], true, out CodeType parsedType))
                    return (parsedType, parts[1]);
                else
                    return (CodeType.Unknown, parts[1]);
            }

            return (CodeType.None, this.Code);
        }


        /// <summary>
        /// Return the ID part of the code
        /// </summary>
        /// <returns></returns>
        public string ExtractID()
        {
            ( _ , string ID) = ExtractCodeTypeAndID();
            return ID;
        }


        /// <summary>
        /// Return the ID part of the code
        /// </summary>
        /// <returns></returns>
        public CodeType ExtractCodeType()
        {
            (CodeType codeType, string ID) = ExtractCodeTypeAndID();
            return codeType;
        }


        /// <summary>
        /// Make the combined SpeciesItem.Code
        /// </summary>
        /// <param name="codetype"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        public static string MakeCodeFromCodeTypeAndCode(CodeType codeType, string code)
        {
            return $"{codeType}:{code}";
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    public class ComboItem
    {
        public string? Key { get; set; }
        public string? DisplayText { get; set; }
    }


    public class SpeciesCodeList
    {

        // Report 
        private Reporter? report = null;

        // Species code list file name
        private string _speciesCodeListFileSpec = "";
        private static readonly object _fileLock = new();


        // Hold the full species code list
        public ObservableCollection<SpeciesItem> SpeciesItems { get; /*set;*/ }


        // Search types
        public enum SearchType
        {
            Family,
            Genus,
            Species,
            Code
        }

        // Indicate if the scientific and common names are present
        public bool IsScientificAndCommonNamePresent { get; set; } = false;

        // Display types
        DisplayType displayType = DisplayType.Scientific | DisplayType.Common;

        // Return the combo items for the Family, Genus, Species
        public ObservableCollection<SpeciesItem> FamilyComboItems { get; } = [];
        public ObservableCollection<SpeciesItem> GenusComboItems { get; } = [];
        public ObservableCollection<SpeciesItem> SpeciesComboItems { get; } = [];


        public SpeciesCodeList()
        {
            SpeciesItems = [];
        }

        /// <summary>
        /// Allow access to the species list code file spec
        /// </summary>
        public string SpeciesCodeListFileSpec 
        {
            get => _speciesCodeListFileSpec;
        }

        /// <summary>
        /// Load the species code list from a file
        /// The species.txt file lives in the local folder. However if it isn't found there
        /// a copy is made from the executable directory. 
        /// </summary>
        /// <param name="fileName"></param>
        public bool Load(string fileName, Reporter? _report, bool trueScientificFalseCommonName)
        {
            bool ret = false;

            report = _report;

            lock (_fileLock)
            {
                // Reset
                SpeciesItems.Clear();
                IsScientificAndCommonNamePresent = false;

                // Make and remember the species code list file spec for saving
                string folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                _speciesCodeListFileSpec = Path.Combine(folderPath, fileName);

                // Check the species code list file exists
                if (!File.Exists(_speciesCodeListFileSpec))
                {
                    // Copy the file from the executable directory
                    string speciesCodeListExecutablePathFileSpec = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!, fileName);
                    File.Copy(speciesCodeListExecutablePathFileSpec, _speciesCodeListFileSpec, true);
                    Debug.WriteLine($"SpeciesCodeList.Load: Species file not found in the local folder. The default version was copied from the executable path.");
                }

                           
                // Try to load all the species code list records
                try
                {
                    ret = true;

                    using (var reader = new StreamReader(_speciesCodeListFileSpec))
                    {

                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            SpeciesItem item = new();

                            if (item.Load(line))
                            {
                                SpeciesItems.Add(item);

                                if (item.IsScientificAndCommonNamePresent)
                                    IsScientificAndCommonNamePresent = true;
                            }
                        }
                    } // <-- file is fully closed here

                    // Sort the species code list
                    string sortedAndSaved = "";
                    if (Sort(trueScientificFalseCommonName))
                    {
                        // If the sort changed anything then save the new list
                        Save();
                        sortedAndSaved = "sorted and saved ";
                    }

                    // Check for duplicates codes
                    var duplicates = GetDuplicateSpeciesCodes();
                    if (duplicates.Count > 0)
                    {
                        string reportText2 = $"SpeciesCodeList.Load: WARNING - Found {duplicates.Count} species items with duplicate codes:";
                        report?.Warning("", reportText2);
                        Debug.WriteLine(reportText2);
                        foreach (var dup in duplicates)
                        {
                            reportText2 = $"Duplicate Code: {dup.Code} | Family: {dup.Family} | Genus: {dup.Genus} | Species: {dup.Species}";
                            report?.Warning("", reportText2);
                            Debug.WriteLine(reportText2);
                        }
                    }

                    // Check for item with missing codes
                    ReportMissingCodesIfOverThreshold(5.0/*5%*/);

                    // Report number of species item loaded
                    string reportText = $"SpeciesCodeList.Load: Loaded {sortedAndSaved}{SpeciesItems.Count} species items";
                    report?.Info("", reportText);
                    Debug.WriteLine(reportText);
                }
                catch (Exception ex)
                {
                    string reportText = $"SpeciesCodeList.Load: Error loading species code list: {ex.Message}";
                    Debug.WriteLine(reportText);
                    report?.Error("", reportText);
                    ret = false;
                }
            }

            return ret;
        }


        /// <summary>
        /// Unload the species code list
        /// </summary>
        public void Unload()
        {

            FamilyComboItems.Clear();
            GenusComboItems.Clear();
            SpeciesComboItems.Clear();

            SpeciesItems.Clear();

            return;
        }

        /// <summary>
        /// Set the display type for the Family, Genus, Species to scientific/common, 
        /// scientific or common name
        /// </summary>
        /// <param name="displayType"></param>
        public void SetDisplayType(DisplayType displayTypeTemp)
        {
            displayType = displayTypeTemp;
        }


        /// <summary>
        /// Search in the species fields for matching species items
        /// </summary>
        /// <param name="searchType"></param>
        /// <returns></returns>
        public bool SearchSpecies(string searchFor, string genus, string family)
        {
            SpeciesComboItems.Clear();

            // Validate that if genis is present that it is valid
            bool isFamilyPresent = SpeciesItems.Any(item => item.Family.Equals(family, StringComparison.OrdinalIgnoreCase));
            if (!isFamilyPresent)
                family = "";

            // Validate that if family is present that it is valid
            bool isGenusPresent = SpeciesItems.Any(item => item.Genus.Equals(genus, StringComparison.OrdinalIgnoreCase));
            if (!isGenusPresent)
                genus = "";

            System.Collections.Generic.IEnumerable<Surveyor.SpeciesItem> matching;

            if (!string.IsNullOrEmpty(searchFor))
            {
                if (!string.IsNullOrEmpty(genus) && !string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => (item.Species?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false) &&
                                       (item.Genus?.Equals(genus, StringComparison.OrdinalIgnoreCase) ?? false) &&
                                       (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else if (!string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => (item.Species?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false) &&                                       
                                       (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else if (!string.IsNullOrEmpty(genus))
                    matching = SpeciesItems
                        .Where(item => (item.Species?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false) &&
                                       (item.Genus?.Equals(genus, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else
                    matching = SpeciesItems
                        .Where(item => (item.Species?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
            }
            else
            {
                if (!string.IsNullOrEmpty(genus) && !string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => (item.Genus?.Equals(genus, StringComparison.OrdinalIgnoreCase) ?? false) &&
                                       (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else if (!string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else if (!string.IsNullOrEmpty(genus))
                    matching = SpeciesItems
                        .Where(item => (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
                else
                    matching = SpeciesItems
                        .OrderBy(item => item.Family)
                        .ThenBy(item => item.Genus)
                        .ThenBy(item => item.Species)
                        .GroupBy(item => item.Species) // Group by Species
                        .Select(group => group.First()); // Select the first item from each group
            }

            
            // Convert matching species to ComboItems
            foreach (var item in matching)
                SpeciesComboItems.Add(item);

            return true;
        }


        /// <summary>
        /// Search the genus list using the searchFor text
        /// Apply the family filter if present
        /// </summary>
        /// <param name="searchFor"></param>
        /// <param name="family"></param>
        /// <returns></returns>
        public bool SearchGenus(string searchFor, string family)
        {
            GenusComboItems.Clear();

            // Validate that if genis is present that it is valid
            bool isFamilyPresent = SpeciesItems.Any(item => item.Family.Equals(family, StringComparison.OrdinalIgnoreCase));
            if (!isFamilyPresent)
                family = "";

            System.Collections.Generic.IEnumerable<Surveyor.SpeciesItem> matching;

            if (!string.IsNullOrEmpty(searchFor))
            {
                if (!string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => (item.Genus?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false) &&
                                       (item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Genus)
                        .GroupBy(item => item.Genus) // Group by Genus
                        .Select(group => group.First()); // Select the first item from each group
                else
                    matching = SpeciesItems
                        .Where(item => (item.Genus?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false))
                        .OrderBy(item => item.Genus)
                        .GroupBy(item => item.Genus) // Group by Genus
                        .Select(group => group.First()); // Select the first item from each group
            }
            else
            {
                if (!string.IsNullOrEmpty(family))
                    matching = SpeciesItems
                        .Where(item => item.Family?.Equals(family, StringComparison.OrdinalIgnoreCase) ?? false)
                        .OrderBy(item => item.Genus)
                        .GroupBy(item => item.Genus) // Group by Genus
                        .Select(group => group.First()); // Select the first item from each group            
                else
                    matching = SpeciesItems
                        .OrderBy(item => item.Genus)
                        .GroupBy(item => item.Genus) // Group by Genus
                        .Select(group => group.First()); // Select the first item from each group            
            }

            // Convert matching species to ComboItems
            foreach (var item in matching)
                GenusComboItems.Add(item);


            return true;
        }


        /// <summary>
        /// Search for the term in the indicated search type and return the
        /// Resulting Family, Genus, Species, Code sub list that flow from the result
        /// the scientific and/or common names are returned if present
        /// </summary>
        /// <param name="searchType"></param>
        /// <returns></returns>
        public bool SearchFamily(string searchFor)
        {
            FamilyComboItems.Clear();

            System.Collections.Generic.IEnumerable<Surveyor.SpeciesItem> matching;

            if (!string.IsNullOrEmpty(searchFor))
                matching = SpeciesItems
                    .Where(item => item.Family?.Contains(searchFor, StringComparison.OrdinalIgnoreCase) ?? false)
                    .OrderBy(item => item.Family)
                    .GroupBy(item => item.Family) // Group by Genus
                    .Select(group => group.First()); // Select the first item from each group            
            else
                matching = SpeciesItems
                    .OrderBy(item => item.Family)
                    .GroupBy(item => item.Family) // Group by Genus
                    .Select(group => group.First()); // Select the first item from each group            


            // Convert matching species to ComboItems
            foreach (var item in matching)
                FamilyComboItems.Add(item);


            return true;
        }


        /// <summary>
        /// Using the actual species name get the SpeciesItem record
        /// </summary>
        /// <param name="species"></param>
        /// <returns></returns>
        public SpeciesItem? GetSpeciesItemBySpeciesName(string species)
        {
            return SpeciesItems.Where(item => item.Species.Equals(species, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }


        /// <summary>
        /// Using the species CodeType and ID get the SpeciesItem record
        /// </summary>
        /// <param name="CodeTypeAndID"></param>
        /// <returns></returns>
        public SpeciesItem? GetSpeciesItemByCodeTypeAndID(string code)
        {
            return SpeciesItems.Where(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }


        /// <summary>
        /// Delete the species item from the list and write adjusted list to the species text file
        /// </summary>
        /// <param name="speciesItem"></param>
        /// <returns></returns>
        public bool DeleteItem(SpeciesItem speciesItem)
        {
            bool ret = false;

            try
            {
                SpeciesItems.Remove(speciesItem);

                ret = Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SpeciesCodeList.DeleteItem: Error deleting species item: {ex.Message}");
                ret = false;
            }

            return ret;
        }


        /// <summary>
        /// Add the species item to the list, sort the list as requested and write adjusted list to the species text file
        /// </summary>
        /// <param name="speciesItem"></param>
        /// <param name="trueScientificFalseCommonName"></param>
        /// <returns></returns>
        public bool AddItem(SpeciesItem speciesItem, bool trueScientificFalseCommonName)
        {
            bool ret = false;

            try
            {
                if (!string.IsNullOrWhiteSpace(speciesItem.Family) && !string.IsNullOrWhiteSpace(speciesItem.Genus) && !string.IsNullOrWhiteSpace(speciesItem.Species))
                {
                    // Add the new species code list item
                    SpeciesItems.Add(speciesItem);

                    // Sort the whole list as required
                    Sort(trueScientificFalseCommonName);

                    // Save back to disk
                    ret = Save();
                    if (ret)
                        Debug.WriteLine($"SpeciesCodeList.AddItem: Updated and saved species item: {speciesItem.Species} {speciesItem.Code}");
                    else
                        Debug.WriteLine($"SpeciesCodeList.AddItem: Updated and failed to save species item: {speciesItem.Species} {speciesItem.Code}");
                }
                else
                {
                    string report = $"SpeciesCodeList.AddItem: A new species record must have at less a Family, Genus and Species. Family={speciesItem.Family}, Genus={speciesItem.Genus} and Species={speciesItem.Species}";

                    Debug.WriteLine(report);
                    ret = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SpeciesCodeList.AddItem: Error adding species item: {ex.Message}");
                ret = false;
            }

            return ret;
        }


        /// <summary>
        /// Update the fields of an 
        /// </summary>
        /// <param name="speciesItem"></param>
        /// <returns></returns>
        public bool UpdateItem(SpeciesItem speciesItem, bool trueScientificFalseCommonName)
        {
            bool ret = false;

            try
            {
                // Check that speciesItem is really an item from the SpeciesItems list (i.e. the reference matches an item in the list)
                var item = SpeciesItems.Where(i => i == speciesItem).FirstOrDefault();

                if (item is not null)
                {
                    // Sort the whole list as required
                    Sort(trueScientificFalseCommonName);

                    // The item has been updated so just save to disk
                    ret = Save();
                    if (ret)
                        Debug.WriteLine($"SpeciesCodeList.UpdateItem: Updated and saved species item: {speciesItem.Species} {speciesItem.Code}");
                    else
                        Debug.WriteLine($"SpeciesCodeList.UpdateItem: Updated and failed to save species item: {speciesItem.Species} {speciesItem.Code}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SpeciesCodeList.UpdateItem: Error updating species item: {ex.Message}");
                ret = false;
            }

            return ret;
        }


        /// <summary>
        /// Sort the species code list either by the Scientific(latin name) or Common name
        /// </summary>
        /// <param name="trueScientificFalseCommonName"></param>
        /// <returns></returns>
        public bool Sort(bool trueScientificFalseCommonName)
        {
            bool anyChanges = false;

            // Create a new sorted list based on the flag
            List<SpeciesItem> sorted;

            //??? Debug.WriteLine the current list fo species name
            //???int i = 0;
            //Debug.WriteLine($"BEFORE SORT");
            //foreach (var item in SpeciesItems)
            //{
            //    Debug.WriteLine($"{i}: {item.Species}");
            //    i++;
            //}

            if (trueScientificFalseCommonName)
            {
                sorted = SpeciesItems
                    .OrderBy(item => item.FamilyScientific)
                    .ThenBy(item => item.Genus)
                    .ThenBy(item => item.SpeciesScientific)
                    .ToList();
            }
            else
            {
                sorted = SpeciesItems
                    .OrderBy(item => item.FamilyCommon)
                    .ThenBy(item => item.Genus)
                    .ThenBy(item => item.SpeciesCommon)
                    .ToList();
            }

            //???i = 0;
            //Debug.WriteLine($"AFTER SORT");
            //foreach (var item in sorted)
            //{
            //    Debug.WriteLine($"{i}: {item.Species}");
            //    i++;
            //}

            // Check if the order has changed
            if (!SpeciesItems.SequenceEqual(sorted))
            {
                SpeciesItems.Clear();
                foreach (var item in sorted)
                    SpeciesItems.Add(item);

                anyChanges = true;
            }

            return anyChanges;
        }


        /// <summary>
        /// Save the species code list to a file
        /// </summary>
        /// <returns></returns>
        public bool Save()
        {
            lock (_fileLock)
            {
                // Save the species code list to a file
                // Assuming file is located in the output directory next to the executable
                string? appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string filePath = _speciesCodeListFileSpec;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Debug.WriteLine($"SpeciesCodeList.Save: No file name specified");
                    return false;
                }

                try
                {
                    using var writer = new StreamWriter(filePath);

                    // Write the header line (this is do stay compatible with EventManager)
                    writer.WriteLine($"family\tgenus\tspecies\tID\t");
                    
                    foreach (var item in SpeciesItems)
                    {
                        string cleanFamily = Clean(item.Family ?? "");
                        string cleanGenus = Clean(item.Genus ?? "");
                        string cleanSpecies = Clean(item.Species ?? "");
                        string cleanCode = Clean(item.Code ?? "");

                        // Skip empty lines
                        if (string.IsNullOrWhiteSpace(cleanFamily) || string.IsNullOrWhiteSpace(cleanGenus) || string.IsNullOrWhiteSpace(cleanSpecies))
                            continue;

                        writer.WriteLine($"{cleanFamily}\t{cleanGenus}\t{cleanSpecies}\t{cleanCode}");
                    }
                    Debug.WriteLine($"SpeciesCodeList.Save: Saved {SpeciesItems.Count} species items");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SpeciesCodeList.Save: Error saving species code list: {ex.Message}");
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Generate a hash for the list
        /// </summary>
        /// <returns></returns>
        public string GetHash()
        {
            // Order items consistently and project key fields to a single string per item
            var lines = SpeciesItems
                .OrderBy(item => item.Family)
                .ThenBy(item => item.Genus)
                .ThenBy(item => item.Species)
                .ThenBy(item => item.Code)
                .Select(item =>
                    $"{item.Family}|{item.Genus}|{item.Species}|{item.Code}");

            // Concatenate all lines with a line separator
            string combined = string.Join('\n', lines);

            // Compute SHA256 hash
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

            // Convert to hex string
            return Convert.ToHexString(hashBytes); // or BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Clean the input string by removing all new lines, tabs and spaces
        /// Make the string save to use in a tab delimited CSV file
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string Clean(string input)
        {
            return input?
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Trim() ?? string.Empty;
        }


        /// <summary>
        /// Returns a list of duplicate species codes
        /// </summary>
        /// <returns></returns>
        private List<SpeciesItem> GetDuplicateSpeciesCodes()
        {
            return SpeciesItems
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Code) &&
                    item.Code.Contains(':') &&
                    !string.IsNullOrWhiteSpace(item.Code.Split(':', 2)[1]) // part after the colon
                )
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToList();
        }


        /// <summary>
        /// Report missing codes if the percentage of missing codes is over the threshold
        /// This is sode we don't genenrate too many messages
        /// </summary>
        /// <param name="percentThreshold"></param>
        private void ReportMissingCodesIfOverThreshold(double percentThreshold = 5.0)
        {
            // A code is considered missing if:
            // - it's null or empty
            // - or it's in the format "XXX:" (i.e., ends with colon and nothing after)
            var missingCodes = SpeciesItems
                .Where(item =>
                    string.IsNullOrWhiteSpace(item.Code) ||
                    (item.Code.Contains(':') && string.IsNullOrWhiteSpace(item.Code.Split(':', 2)[1]))
                )
                .ToList();

            int total = SpeciesItems.Count;
            int missingCount = missingCodes.Count;
            double missingPercentage = (total == 0) ? 0 : (missingCount * 100.0 / total);

            // If there are species items missing a code then record the number of item
            // if there aren't too many that list them all
            if (missingCount > 0)
            {
                string reportText = $"WARNING: {missingCount} of {total} species items ({missingPercentage:F2}%) have missing codes";
                report?.Warning("", reportText);
                Debug.WriteLine(reportText);
                if (missingPercentage < percentThreshold)
                {
                    foreach (var item in missingCodes)
                    {
                        reportText = $"Missing Code -> Family: {item.Family}, Genus: {item.Genus}, Species: {item.Species}, Code: \"{item.Code}\"";
                        report?.Warning("", reportText);
                        Debug.WriteLine(reportText);
                    }
                }
            }
        }
    }
}
