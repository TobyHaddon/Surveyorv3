// Surveyor SpeciesCodeList
// Manages the loaded and the selected species code list
// 
// Version 1.0


using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static Surveyor.SpeciesItem;
using System;
using Windows.System.Profile;
using System.Linq;
using System.Diagnostics;
using Microsoft.VisualBasic;
using System.Reflection;
using Windows.ApplicationModel.Background;

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
            Fishbase
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
        /// e.g. Fishbase:3452 is CodeType.FishBase Code = 3452
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public (CodeType codeType, string code) GetCodeTypeAndCode()
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
        // Hold the full species code list
        public List<SpeciesItem> SpeciesItems { get; set; }

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
        public ObservableCollection<SpeciesItem> FamilyComboItems { get; } = new();
        public ObservableCollection<SpeciesItem> GenusComboItems { get; } = new();
        public ObservableCollection<SpeciesItem> SpeciesComboItems { get; } = new();


        public SpeciesCodeList()
        {
            SpeciesItems = new List<SpeciesItem>();
        }


        /// <summary>
        /// Load the species code list from a file
        /// </summary>
        /// <param name="fileName"></param>
        public void Load(string fileName)
        {
            // Reset
            SpeciesItems.Clear();
            IsScientificAndCommonNamePresent = false;

            // Assuming file is located in the output directory next to the executable
            string? appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string filePath = Path.Combine(appDirectory!, fileName);

            try
            {
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

                Debug.WriteLine($"SpeciesCodeList.Load: Loaded {SpeciesItems.Count} species items");
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"SpeciesCodeList.Load: Error loading species code list: {ex.Message}");
            }
        }


        /// <summary>
        /// Unload the species code list
        /// </summary>
        public void Unload()
        {
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

        public SpeciesItem? GetSpeciesItem(string species)
        {
            return SpeciesItems.Where(item => item.Species.Equals(species, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        }

    }
}
