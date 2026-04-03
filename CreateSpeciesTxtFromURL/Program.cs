using Microsoft.UI.Xaml.Shapes;
using Surveyor.Helper;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Windows.Storage;

namespace Surveyor;

internal static class Program
{
    private static readonly HttpClient httpClient = new();

    private enum Direction { Upload, Download }
    private enum TransferType { Page, File }
    private enum Status { Required, Downloaded, Uploaded, Failed }

    private enum Output { Tabular, Detailed }

    public static async Task<int> Main(string[] args)
    {
        int ret = 0;
        string urlBase = "https://www.fishbase.se";
        Output output = Output.Tabular;

        if (args.Length > 0 && (args[0] == "?" || string.Compare(args[0], "/h", true) == 0))
        {
            Console.WriteLine("Usage: CreateSpeciesTxtFromURL <fishbase url> [/D]");
            Console.WriteLine("If <fishbase url> is not included the default is fishbase.se");
            Console.WriteLine("    /D  output in detailed form instead of the default tabular format");
            Console.WriteLine("");
            Console.WriteLine("The utility retrieves family and the FishBase ID from FishBase");
            Console.WriteLine("It expects to receive input from the console in the form of genus species <Enter>");
            Console.WriteLine("The genus and species can be tab, dot or comma delimited");
            Console.WriteLine("A text file with a list of multiple genus species can be piped in e.g.:");
            Console.WriteLine("    CreateSpeciesTxtFromURL <genusspecies.txt >species.txt");
            Console.WriteLine(" ");
            Console.WriteLine("Example Input File:");
            Console.WriteLine("Genus,species");
            Console.WriteLine("Amblyglyphidodon,curacao");
            Console.WriteLine("Dascyllus,reticulatus");
            Console.WriteLine("");
            Console.WriteLine("Resulting Output File:");
            Console.WriteLine("Family\tGenus\tSpecies\tFishBaseID");

            return 1;
        }

        // Check for optional parameters
        foreach (string arg in args)
        {
            if (arg.Equals("/D", StringComparison.OrdinalIgnoreCase))
            {
                output = Output.Detailed;
            }
            else if (Uri.IsWellFormedUriString(arg, UriKind.Absolute))
            {
                urlBase = arg.TrimEnd('/');
            }
        }

        bool checkedHeader = false;
        string? line;

        // Read input from the console , exit on EOF
        while ((line = Console.ReadLine()) is not null)
        {
            line = line.Trim();

            // If a line is empty, skip
            if (line.Length == 0)
                continue;

            // Allow inputs like:
            // genus.species
            // genus\tspecies
            // genus,species
            // genus species
            string[] parts = line.Split(['\t', '.', ',', ' '], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            string genus = parts[0].Trim();
            string species = parts[1].Trim();

            // If the first data line is a title line, skip it (i.e. genus species)
            if (!checkedHeader)
            {
                checkedHeader = true;
                if (string.Equals(genus, "genus", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Parse the extract genus and species to the function to get the page from the web
            HtmlFishBaseParser.HtmlFishBaseSpeciesMetadata? metaData = await GetFishBaseDataFromGenusAndSpecies(urlBase, genus, species, output);

            if (metaData is not null && (!string.IsNullOrEmpty(metaData.FamilyLatin) && !string.IsNullOrEmpty(metaData.Genus) && !string.IsNullOrEmpty(metaData.SpeciesLatin)))
            {
                if (output == Output.Detailed)
                {
                    Console.WriteLine($"Genus: {metaData.Genus}");
                    Console.WriteLine($"Species: {metaData.SpeciesLatin} ({metaData.SpeciesCommon})");
                    Console.WriteLine($"Family: {metaData.FamilyLatin} ({metaData.FamilyCommon})");
                    Console.WriteLine($"FishBase ID: {metaData.FishID}");
                    Console.WriteLine($"Distribution: {metaData.Distribution}");
                    Console.WriteLine($"Environment: {metaData.Environment}");
                    Console.WriteLine($"Size: {metaData.SpeciesSize}");
                    Console.WriteLine();
                }
                else if (output == Output.Tabular)
                {
                    WriteTabularLine(metaData.FamilyLatin ?? "null", metaData.FamilyCommon ?? "null", metaData.Genus ?? "null", metaData.SpeciesLatin ?? "null", metaData.SpeciesCommon ?? "null", metaData.FishID);
                }
            }
            else
            {
                Console.Error.WriteLine($"***Failed to retrieve data for {genus} {species}");
                if (output == Output.Tabular)
                {
                    if (metaData is not null)
                        WriteTabularLine(metaData.FamilyLatin ?? "null", metaData.FamilyCommon ?? "null", genus, metaData.SpeciesLatin ?? "null", species, metaData.FishID);
                    else
                        WriteTabularLine("null", "null", genus, "null", species, null);
                }
            }
        }

        return ret;
    }

    static bool firstLine = true;
    private static void WriteTabularLine(string familyLatin, string familyCommon, string genus, string speciesLatin, string speciesCommon, int? fishBaseID)
    {
        if (firstLine)
        {
            // family	genus	species	CAAB
            Console.WriteLine("family\tgenus\tspecies\tCAAB");
            firstLine = false;
        }

        StringBuilder sb = new();
        // Family
        sb.Append(familyLatin);
        if (!string.IsNullOrWhiteSpace(familyCommon))
        {
            sb.Append('/');
            sb.Append(familyCommon);
        }
        sb.Append('\t');
        // Genus
        sb.Append(genus);
        sb.Append('\t');
        // Species
        sb.Append(speciesLatin);
        if (!string.IsNullOrWhiteSpace(speciesCommon))
        {
            sb.Append('/');
            sb.Append(speciesCommon);
        }
        sb.Append('\t');
        // FishBase ID  
        if (fishBaseID is not null)
        {
            sb.Append("Fishbase:");
            sb.Append(fishBaseID);
        }
        Console.WriteLine(sb);
    }

    private static async Task<HtmlFishBaseParser.HtmlFishBaseSpeciesMetadata?> GetFishBaseDataFromGenusAndSpecies(string urlBase, string genus, string species, Output output)
    {
        HtmlFishBaseParser.HtmlFishBaseSpeciesMetadata? metadata = null;

        TransferType transferType = TransferType.Page;
        string relativeLocalFileSpec = $"{genus}_{species}.html";

        // https://www.fishbase.se/summary/Chromis_ternatensis.html
        string url = $"{urlBase}/summary/{genus}_{species}.html";

        Status result = await GetURLPageOrFileToLocalFile(url, transferType, relativeLocalFileSpec);

        if (result == Status.Downloaded && transferType == TransferType.Page)
        {
            string fullPath = LocalFolderHelper.GetFullPath(relativeLocalFileSpec);

            metadata = await HtmlFishBaseParser.ParseHtmlFishbaseSummaryAndExtractSpeciesMetadataAsync(fullPath);

            File.Delete(fullPath);
        }

        return metadata;
    }

    private static async Task<Status> GetURLPageOrFileToLocalFile(string url, TransferType urlType, string relativeLocalFileSpec)
    {
        Status ret = Status.Required;

        var response = await GetWithBackoffAsync(url);
        response.EnsureSuccessStatusCode();

        await LocalFolderHelper.EnsureLocalSubfolderPathExistsAsync(relativeLocalFileSpec);

        string fullPath = LocalFolderHelper.GetFullPath(relativeLocalFileSpec);

        if (urlType == TransferType.Page)
        {
            string content = await response.Content.ReadAsStringAsync();
            await File.WriteAllTextAsync(fullPath, content);
        }
        else if (urlType == TransferType.File)
        {
            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(fullPath, imageBytes);
        }

        ret = Status.Downloaded;
        return ret;
    }

    /// <summary>
    /// Used in place of httpClient.GetAsync() that automatically retries with
    /// exponential back off only if it gets 403 Forbidden.
    /// </summary>
    private static async Task<HttpResponseMessage> GetWithBackoffAsync(string url)
    {
        const int maxRetries = 5;
        TimeSpan delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var response = await httpClient.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                return response;
            }

            if (attempt == maxRetries)
            {
                return response;
            }

            await Task.Delay(delay);
            delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
        }

        throw new InvalidOperationException("Unreachable code in Program.GetWithBackoffAsync.");
    }
}