using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Windows.Storage;
using HtmlAgilityPack;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.IO;


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
        public static HtmlFishBaseImageMetadata ParseHtmlFishbasePhotoPage(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return ParseHtmlFishbasePhotoPage(stream);
            }
        }

        public static HtmlFishBaseImageMetadata ParseHtmlFishbasePhotoPage(Stream stream)
        {
            var result = new HtmlFishBaseImageMetadata();


            var doc = new HtmlDocument();
            doc.Load(stream);

            // 1. Extract image src from <!--image section-->
            var imageSection = doc.DocumentNode
                .SelectSingleNode("//div[comment()[contains(.,'image section')]]//img");
            if (imageSection != null)
            {
                result.ImageSrc = imageSection.GetAttributeValue("src", "");
            }

            // 2. Extract genusSpecies from <i> inside <!--image caption section-->
            var captionSection = doc.DocumentNode
                .SelectSingleNode("//div[comment()[contains(.,'image caption section')]]//i");
            if (captionSection != null)
            {
                result.GenusSpecies = captionSection.InnerText.Trim();
            }

            // 3. Extract author (the <a> tag after 'by') inside <!--image caption section-->
            var authorLink = doc.DocumentNode
                .SelectSingleNode("//div[comment()[contains(.,'image caption section')]]//a[contains(@href,'CollaboratorSummary')]");
            if (authorLink != null)
            {
                result.Author = authorLink.InnerText.Trim();
            }

            // 4. Extract total number of images from <!--page navigation-->
            var pageNavTextNode = doc.DocumentNode
                .SelectSingleNode("//div[comment()[contains(.,'page navigation')]]");
            if (pageNavTextNode != null)
            {
                var text = pageNavTextNode.InnerText;
                var match = Regex.Match(text, @"(\d+)\s+of\s+(\d+)");
                if (match.Success)
                {
                    result.TotalImages = int.Parse(match.Groups[2].Value);
                }
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
        public static async Task<HtmlFishBaseSpeciesMetadata> ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return await ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(stream);
            }
        }

        public static async Task<HtmlFishBaseSpeciesMetadata> ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(Stream stream)
        {
            using var reader = new StreamReader(stream);

            string content = await reader.ReadToEndAsync();

            // Attempt to locate the start of the HTML document
            int startIndex = content.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
            if (startIndex > 0)
            {
                // Before loading the HTML into HtmlDocument, strip out any leading garbage
                content = content.Substring(startIndex);
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var result = new HtmlFishBaseSpeciesMetadata();

            // --- Genus, SpeciesLatin, SpeciesCommon ---
            var genusSpeciesNodes = doc.DocumentNode.SelectNodes("//div[@id='ss-sciname']//span[@class='sciname']//a");
            if (genusSpeciesNodes != null && genusSpeciesNodes.Count >= 2)
            {
                result.Genus = genusSpeciesNodes[0].InnerText.Trim();
                result.SpeciesLatin = genusSpeciesNodes[1].InnerText.Trim();
            }

            // Common name
            var commonNameNode = doc.DocumentNode.SelectSingleNode("//div[@id='ss-sciname']//span[contains(@class, 'sheader2')]");
            if (commonNameNode != null)
            {
                result.SpeciesCommon = HtmlEntity.DeEntitize(commonNameNode.InnerText.Trim());
            }


            // --- FamilyLatin & FamilyCommon ---
            var classifcationNode = doc.DocumentNode.SelectSingleNode("//div[@id='ss-main']//div[contains(@class, 'smallSpace')]");
            if (classifcationNode is not null)
            {
                // Extract the family latin name
                var familyLatin = classifcationNode.SelectSingleNode("//a[contains(@href, '/summary/FamilySummary.php')]");
                if (familyLatin is not null)
                {
                    result.FamilyLatin = familyLatin.InnerText.Trim();
                }

                // Find all <span class="slabel1 ">...</span> inside classificationNode
                var spanNodes = classifcationNode.SelectNodes(".//span[@class='slabel1 ']");

                if (spanNodes != null && spanNodes.Count >= 2)
                {
                    var secondSpan = spanNodes[1];

                    // Get the sibling text node immediately after the second <span>
                    var nextNode = secondSpan.NextSibling;
                    while (nextNode != null && nextNode.NodeType != HtmlNodeType.Text)
                    {
                        nextNode = nextNode.NextSibling;
                    }

                    if (nextNode != null)
                    {
                        var rawText = HtmlEntity.DeEntitize(nextNode.InnerText); // e.g., " (Damselfishes) > Chrominae"

                        if (rawText is not null)
                        {
                            // Extract the text inside the first pair of round brackets
                            var match = Regex.Match(rawText, @"\(([^)]+)\)");
                            if (match.Success)
                            {
                                result.FamilyCommon = match.Groups[1].Value.Trim(); // "Damselfishes"
                            }
                        }
                    }
                }
            }

            // --- FishID ---
            var specCodeMatch = Regex.Match(content, @"speccode=(\d+)", RegexOptions.IgnoreCase);
            if (specCodeMatch.Success && int.TryParse(specCodeMatch.Groups[1].Value, out int fishId))
            {
                result.FishID = fishId;
            }


            // --- Helper: clean a div content ---
            static string CleanSpanInnerText(HtmlNode? node)
            {
                if (node == null) return string.Empty;

                var clone = node.Clone();
                if (clone is null || clone.InnerText is null)
                    return string.Empty;

                foreach (var a in clone.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
                    a.Remove();
                
                string? ret = HtmlEntity.DeEntitize(clone.InnerText);
                if (ret is not null)
                {
                    // Remove &nbsp
                    ret = ret.Replace("&nbsp", " ");

                    // Replace non-breaking spaces (\u00A0) with regular space
                    ret = ret.Replace('\u00A0', ' ');

                    // Optionally, normalize multiple whitespace (tabs, newlines, etc.) into single spaces
                    ret = Regex.Replace(ret, @"\s+", " ").Trim();

                    // Remove unwanted trailing text
                    ret = ret.Replace("(Ref. )", "").TrimEnd();
                }
                else
                    return string.Empty;

                return ret;
            }


            // --- Environment ---
            var envNode = doc.DocumentNode.SelectSingleNode("//h1[contains(text(), 'Environment')]/following-sibling::div[@class='smallSpace']//span");
            result.Environment = CleanSpanInnerText(envNode);

            // --- Distribution ---
            var distNode = doc.DocumentNode.SelectSingleNode("//h1[contains(text(), 'Distribution')]/following-sibling::div[@class='smallSpace']//span");
            result.Distribution = CleanSpanInnerText(distNode);

            // --- SpeciesSize ---
            var sizeNode = doc.DocumentNode.SelectSingleNode("//h1[contains(text(), 'Size')]/following-sibling::div[@class='smallSpace']//span");
            result.SpeciesSize = CleanSpanInnerText(sizeNode);

            return result;
        }
    }
}
