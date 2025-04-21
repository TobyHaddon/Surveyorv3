using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace Surveyor.Helper
{


    public class HtmlFishBaseImageMetadata
    {
        public string? ImageSrc { get; set; }
        public string? GenusSpecies { get; set; }
        public string? Author { get; set; }
        public int? TotalImages { get; set; }
    }

    public class HtmlFishBaseParser
    {
        /// <summary>
        /// Extract the iamge url, the genus and species, the auther and the total number of images available for this species
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<HtmlFishBaseImageMetadata> ParseHtmlFishbasePhotoPage(StorageFile file)
        {
            string content = await FileIO.ReadTextAsync(file);

            var result = new HtmlFishBaseImageMetadata();

            // Extract image src from <!--image section-->
            //???var imageMatch = Regex.Match(content, "<!--image section-->.*?<img[^>]*src=\"(.*?)\"", RegexOptions.Singleline);
            var imageMatch = Regex.Match(content, @"<!--image section-->.*?<img[^>]*src=['""]([^'""]+)['""]", RegexOptions.Singleline);

            if (imageMatch.Success)
            {
                result.ImageSrc = imageMatch.Groups[1].Value;
            }

            // Extract genusSpecies in <i> inside <!--image caption section-->
            var genusMatch = Regex.Match(content, @"<!--image caption section-->.*?<i>(.*?)</i>", RegexOptions.Singleline);
            if (genusMatch.Success)
            {
                result.GenusSpecies = genusMatch.Groups[1].Value;
            }

            // Extract author in <a> after 'by' inside <!--image caption section-->
            var authorMatch = Regex.Match(content, @"<!--image caption section-->.*?by\s*<a[^>]*>(.*?)</a>", RegexOptions.Singleline);
            if (authorMatch.Success)
            {
                result.Author = authorMatch.Groups[1].Value;
            }

            // Extract totalImages from <!--page navigation--> x of n
            var totalMatch = Regex.Match(content, @"<!--page navigation-->.*?(\d+)\s+of\s+(\d+)", RegexOptions.Singleline);
            if (totalMatch.Success)
            {
                result.TotalImages = int.Parse(totalMatch.Groups[2].Value);
            }

            return result;
        }


        /// <summary>
        /// Extract the SpeciesID from a species summary page the SpeciesID is found by
        /// finding the speccode= value in the page
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<int?> ParseHtmlFishbaseSummaryAndExtractSpeciesId(StorageFile file)
        {
            string content = await FileIO.ReadTextAsync(file);

            // Match common speccode= pattern, found in many links and parameters
            var speccodeMatch = Regex.Match(content, @"speccode=(\d+)", RegexOptions.IgnoreCase);
            if (speccodeMatch.Success && int.TryParse(speccodeMatch.Groups[1].Value, out int speciesId))
            {
                return speciesId;
            }

            return null;
        }


        public class HtmlFishBaseSpeciesMetadata
        {
            public string? Genus { get; set; }
            public string? SpeciesLatin { get; set; }
            public string? SpeciesCommon { get; set; }
            public string? FamilyLatin { get; set; }
            public string? FamilyCommon { get; set; }
            public int? FishID { get; set; }
            public string? Environment { get; set; }
            public string? Distribution { get; set; }
            public string? SpeciesSize { get; set; }
        }

        /// <summary>
        /// Extract the species metadata from a species summary page
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<HtmlFishBaseSpeciesMetadata> ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(StorageFile file)
        {
            string content = await FileIO.ReadTextAsync(file);

            var result = new HtmlFishBaseSpeciesMetadata();

            // --- Genus, SpeciesLatin, SpeciesCommon ---
            var genusSpeciesMatch = Regex.Match(content, @"<div id=""ss-sciname"".*?<span class=""sciname"">\s*<a[^>]*?>(.*?)</a>\s*</span>\s*<span class=""sciname"">\s*<a[^>]*?>(.*?)</a>\s*</span>.*?<span class=""sheader2"">\s*(.*?)\s*</span>", RegexOptions.Singleline);
            if (genusSpeciesMatch.Success)
            {
                result.Genus = genusSpeciesMatch.Groups[1].Value;
                result.SpeciesLatin = genusSpeciesMatch.Groups[2].Value;
                result.SpeciesCommon = genusSpeciesMatch.Groups[3].Value;
            }

            // --- FamilyLatin & FamilyCommon ---
            var classificationSection = Regex.Match(content, @"<h1[^>]*?>\s*Classification\s*/\s*Names\s*</h1>.*?<div class=""smallSpace"".*?<span class=""slabel1 "">.*?</span>.*?<span class=""slabel1 "">.*?<a[^>]*>(.*?)</a>\s*</span>\s*\(([^)]*)\)", RegexOptions.Singleline);
            if (classificationSection.Success)
            {
                result.FamilyLatin = classificationSection.Groups[1].Value;
                result.FamilyCommon = classificationSection.Groups[2].Value;
            }

            // --- FishID ---
            var speccodeMatch = Regex.Match(content, @"speccode=(\d+)", RegexOptions.IgnoreCase);
            if (speccodeMatch.Success && int.TryParse(speccodeMatch.Groups[1].Value, out int fishId))
            {
                result.FishID = fishId;
            }

            // Helper: Strip HTML tags and unescape
            static string CleanHtml(string html)
            {
                string noLinks = Regex.Replace(html, @"<a[^>]*?>.*?</a>", "", RegexOptions.Singleline);
                string withLineBreaks = Regex.Replace(noLinks, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                return Regex.Replace(withLineBreaks, "<.*?>", "").Trim();
            }

            // --- Environment ---
            var envMatch = Regex.Match(content, @"<h1[^>]*?>\s*Environment:\s*</h1>\s*<div class=""smallSpace"">\s*<span[^>]*?>(.*?)</span>", RegexOptions.Singleline);
            if (envMatch.Success)
            {
                result.Environment = CleanHtml(envMatch.Groups[1].Value);
            }

            // --- Distribution ---
            var distMatch = Regex.Match(content, @"<h1[^>]*?>\s*Distribution\s*</h1>\s*<div class=""smallSpace"">\s*<span[^>]*?>(.*?)</span>", RegexOptions.Singleline);
            if (distMatch.Success)
            {
                result.Distribution = CleanHtml(distMatch.Groups[1].Value);
            }

            // --- SpeciesSize ---
            var sizeMatch = Regex.Match(content, @"<h1[^>]*?>\s*Length at first maturity\s*</h1>\s*<div class=""smallSpace"">\s*<span[^>]*?>(.*?)</span>", RegexOptions.Singleline);
            if (sizeMatch.Success)
            {
                result.SpeciesSize = CleanHtml(sizeMatch.Groups[1].Value);
            }

            return result;
        }

    }
}
