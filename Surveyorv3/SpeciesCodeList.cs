// Surveyor SpeciesCodeList
// Manages the loaded and the selected species code list
// 
// Version 1.0
//
// Version 1.1 20 Apr 2025
// Added the ability to new, update and delete species items (and write to disk)


using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.System.Profile;
using static Surveyor.SpeciesItem;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace Surveyor
{
    public class SpeciesItem
    {
        public string Family { get; set; } = "";
        public string Genus { get; set; } = "";
        public string Species { get; set; } = "";
        public string Code { get; set; } = "";

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
            if (parts.Length != 4)
                return false;


            Family = parts[0];
            Genus = parts[1];
            Species = parts[2];
            Code = parts[3];

            if (string.Compare(Family, "Family", true) == 0)
                return false; // Skip the header line

            // Check for scientific and common names in the family
            (FamilyScientific, FamilyCommon) = SplitField(Family);
            if (FamilyScientific != null && FamilyCommon != null)
                IsScientificAndCommonNamePresent = true;

            // Check for scientific and common names in the species
            (SpeciesScientific, SpeciesCommon) = SplitField(Species);
            if (FamilyScientific != null && FamilyCommon != null)
                IsScientificAndCommonNamePresent = true;

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
                return (parts[0], parts[1]);
            }

            return ("", "");
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
    }


    public class ComboItem
    {
        public string? Key { get; set; }
        public string? DisplayText { get; set; }
    }


    public class SpeciesCodeList
    {
        // Species code list file name
        public string SpeciesCodeListFileSpec = "";
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
        /// Load the species code list from a file
        /// </summary>
        /// <param name="fileName"></param>
        public bool Load(string fileName, bool trueScientificFalseCommonName)
        {
            bool ret = false;

            lock (_fileLock)
            {
                // Reset
                SpeciesItems.Clear();
                IsScientificAndCommonNamePresent = false;

                // Assuming file is located in the output directory next to the executable
                string? appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string filePath = Path.Combine(appDirectory!, fileName);

                // Remember the species code list file spec for saving
                SpeciesCodeListFileSpec = filePath;

                // Try to load all the species code list records
                try
                {
                    ret = true;

                    using var reader = new StreamReader(filePath);

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

                    // Sort the species code list
                    if (Sort(trueScientificFalseCommonName))
                    {
                        // If the sort changed anything then save the new list
                        Save();
                    }

                    Debug.WriteLine($"SpeciesCodeList.Load: Loaded {SpeciesItems.Count} species items");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SpeciesCodeList.Load: Error loading species code list: {ex.Message}");
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
                // Add the new species code list item
                SpeciesItems.Add(speciesItem);

                // Sort the whole list as required
                Sort(trueScientificFalseCommonName);

                // Save back to disk
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
                    return Save();
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
                string filePath = SpeciesCodeListFileSpec;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Debug.WriteLine($"SpeciesCodeList.Save: No file name specified");
                    return false;
                }

                try
                {
                    using var writer = new StreamWriter(filePath);

                    // Write the header line (this is do stay compatible with EventManager)
                    writer.WriteLine($"family\tgenus\tspecies\tCAAB");
                    
                    foreach (var item in SpeciesItems)
                    {
                        string cleanFamily = Clean(item.Family);
                        string cleanGenus = Clean(item.Genus);
                        string cleanSpecies = Clean(item.Species);
                        string cleanCode = Clean(item.Code);

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
    }
}
