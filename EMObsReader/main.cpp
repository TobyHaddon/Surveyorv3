// EMOBReader.cpp : This file contains the 'main' function. Program execution begins and ends there.
//


#define _SILENCE_CXX17_CODECVT_HEADER_DEPRECATION_WARNING
#include <windows.h>  // For GetFileAttributesW and file attribute constants
#include <iostream>
#include <vector>
#include <string>
#include <filesystem>
#include <fstream>
#include <regex>
#include <list>
#include <sstream>
#include <locale>
#include <codecvt>  // For std::wstring_convert
#include "EMObsReader.h"
#include "FileFind.h"
#include "FileMapping.h"

namespace fs = std::filesystem;


#pragma pack(push, 1) // Save the current alignment setting and set alignment to 1 byte
struct MyStruct {
    char a; // 1 byte
    int b;  // 4 bytes, but no padding due to byte alignment
    char c; // 1 byte
};
#pragma pack(pop) // Restore the previous alignment setting


struct _Config
{
	std::string searchPath;
	std::string fileSpec;
	bool searchSubdirs = false;
	fs::path outputFileData;
    fs::path outputFileTLCList;
    fs::path outputFileTLCHierarchy;
    fs::path outputFileHexDump;
    bool dataMode = true;
	bool appendMode = false;
	bool tlcMode = false;
	bool tlcHierarchyMode = false;
	bool hexDumpMode = false;
    fs::path fileMappingFileSpec;
};


struct _Config* parseArguments(int argc, char* argv[]);
std::string convertWildcardToRegex(const std::string& wildcard);
void searchFiles(const std::string& fileSpec, struct _Config* Config, FileMapping fileMapping);
std::wstring ReplaceTabs(const std::wstring& input);
std::wstring RowTypeToString(RowType type);
int ExtractEMObsFileTLCs(const std::string foundFile, std::wofstream& outputFileStream, std::list<struct _OutputTLC*>& outputTLCsAdd);
int ExtractEMObsFileTLCsDisplayHierarchy(const std::string foundFile, std::wofstream& outputFileStream);
int HexDumpEMObsFile(const std::string foundFile, std::wofstream& outputFileStream);




int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cout << "Usage: <program> <filespec> [/s] [/o:<outputfile>] [/a] [/t] [/th] [/h] [/nd]" << std::endl;
        std::cout << "                            /s                 search sub-directories" << std::endl;
        std::cout << "                            /o                 output to EMObs_TLCList.txt" << std::endl;
        std::cout << "                            /o:<outputfile>]   output to outputfile" << std::endl;
        std::cout << "                            /a                 append to output file" << std::endl;
        std::cout << "                            /t                 additionally export the TLC (three letter codes)" << std::endl;
        std::cout << "                            /th                additionally export the TLCs in their hierarchy" << std::endl;
        std::cout << "                            /h                 additionally dump file to hex in the output file" << std::endl;
		std::cout << "                            /no                don't export the data" << std::endl;
        std::cout << "                            /f:<filemapping>]  two column tab delimited text file to map EMObs video file name to new file name" << std::endl; 
        return 1;
    }

    // Variables to hold command-line arguments
    struct _Config* config;

    // Parse the arguments	
    config = parseArguments(argc, argv);

    if (config->searchPath.empty() || config->fileSpec.empty()) {
        std::cerr << "Error: Invalid file spec. Please specify a valid search path and file pattern." << std::endl;
        return 1;
    }

    if (config->outputFileData.empty())
    {
        std::cout << "Use /O:<filespec> to output a tab delimited file." << std::endl;
    }

    if (config->tlcMode == true) {
        std::cout << "TLC mode enabled. Additionally export the TLC (three letter codes) to:[" << config->outputFileTLCList << + "]" << std::endl;
    }
    if (config->tlcHierarchyMode == true) {
        std::cout << "TLC Hierarchy mode enabled. Additionally export TLC (three letter code) in their hierarchy to :[" << config->outputFileTLCHierarchy << + "]" << std::endl;
    }
    if (config->hexDumpMode == true) {
        std::cout << "Additionally create a hex dump to:[" << config->outputFileHexDump << +"]" << std::endl;
    }
    else {
		std::cout << "Normal mode enabled. All data will be extracted" << std::endl;
	}

    
	FileMapping fileMapping(config->fileMappingFileSpec.string());


    // Search for files based on the specified arguments
    searchFiles(convertWildcardToRegex(config->fileSpec), config, fileMapping);

    return 0;
}



// Function to parse arguments
struct _Config* parseArguments(int argc, char* argv[]) {

	struct _Config* config = new struct _Config();

    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];

        if (i == 1) {
            // Convert the filespec to a std::filesystem::path object
            fs::path fullPath(arg);

            // Extract the path (without the filename)
            fs::path path = fullPath.parent_path();

            // Extract the filename with extension
            fs::path file = fullPath.filename();

            config->searchPath = path.string();
			config->fileSpec = file.string();
        }
        else {
            // /s switch for sub-directory searching
            if (arg == "/s" || arg == "/S") {
                config->searchSubdirs = true;
            }

            // /A switch for append mode
            if (arg == "/a" || arg == "/A") {
                config->appendMode = true;
            }

            // Exclusive modes (can only have one of these)
            // /T switch for TLC reporting only
            if (arg == "/T" || arg == "/t") {
                config->tlcMode = true;
            }
            // /TH switch for TLC Hierarchy reporting only
            if (arg == "/TH" || arg == "/th") {
                config->tlcHierarchyMode = true;
            }
            // /H switch for dump file to hex output
            if (arg == "/H" || arg == "/h") {
                config->hexDumpMode = true;
            }

			// no switch to indicate no data is to be exported
            if (arg == "/NO" || arg == "/no") {
                config->dataMode = false;
            }
            else {
                config->dataMode = true;
			}

            // /O:<filespec> switch for output file
            if (arg.find("/o:") == 0 || arg.find("/O:") == 0) {
                config->outputFileData = arg.substr(3);  // Extract the file name after "/O:"
            }

            // /F:<filespec> switch for file mapping file
            if (arg.find("/f:") == 0 || arg.find("/F:") == 0) {
                config->fileMappingFileSpec = arg.substr(3);  // Extract the file name after "/F:"
            }
        }
    }

    

    fs::path baseFileSpec;
    if (!config->outputFileData.empty()) {
        fs::path fulloutputFileData = fs::absolute(config->outputFileData);

        // Get the directory from the full path and the file st
        fs::path outputDirectory = fulloutputFileData.parent_path();
        fs::path outputStem = fulloutputFileData.stem();

        baseFileSpec = outputDirectory / outputStem;
    }
    else {
        // Extract the directory from argv[0] (the program path)
        std::string programPath = argv[0];

        // Get the absolute path of the program
        fs::path fullProgramPath = fs::absolute(programPath);

        // Get the directory from the full path and the file st
        fs::path outputDirectory = fullProgramPath.parent_path();
        
        baseFileSpec = outputDirectory / "EMObs";		
    }


    // Prepare the additional export/dump file specs
    if (config->tlcMode)
        config->outputFileTLCList = baseFileSpec.string() + "_TLCList.txt";
    if (config->tlcHierarchyMode)
        config->outputFileTLCHierarchy = baseFileSpec.string() + "_TLCHierarchy.txt";
    if (config->hexDumpMode)
        config->outputFileHexDump = baseFileSpec.string() + "_HexDump.txt";


	// If no output file is specified, use the default
    if (config->outputFileData.empty()) {
        // There isn't a specifed output file
        if (config->tlcMode)
            config->outputFileTLCList = baseFileSpec.string() + "_TLCList.txt";
        if (config->tlcHierarchyMode)
            config->outputFileTLCHierarchy = baseFileSpec.string() + "_TLCHierarchy.txt";
        if (config->hexDumpMode)
            config->outputFileHexDump = baseFileSpec.string() + "_HexDump.txt";

        config->outputFileData = baseFileSpec.string() + "_Data.txt";        
    }

	// If no file mapping file is specified, use the default
    if (config->fileMappingFileSpec.empty()) {
        config->fileMappingFileSpec = baseFileSpec.string() + "_FileMapping.txt";
    }

	return config;
}


// Function to convert a wildcard pattern like "*.EMObs" to a regex pattern
std::string convertWildcardToRegex(const std::string& wildcard) {
    std::string regexPattern = "^"; // Start of the line anchor
    for (char ch : wildcard) {
        switch (ch) {
        case '*':
            regexPattern += ".*"; // Replace * with ".*" for regex
            break;
        case '?':
            regexPattern += '.';  // Replace ? with "." for regex
            break;
        case '.':
            regexPattern += "\\."; // Escape the dot
            break;
        default:
            regexPattern += ch;    // Regular characters remain the same
            break;
        }
    }
    regexPattern += "$"; // End of the line anchor
    return regexPattern;
}

// Function to perform the search
void searchFiles(const std::string& fileSpec, struct _Config* Config, FileMapping fileMapping) {
    std::wofstream outputFileDataStream;
	std::wofstream outputFileTLCListStream;
	std::wofstream outputFileTLCHierarchyStream;
	std::wofstream outputFileHexDumpStream;

    // Check if the output file already exists
    bool fileExists = fs::exists(Config->outputFileData);

    // Open the EMObs data export file
    if (Config->dataMode == true && !Config->outputFileData.empty()) {
        std::ios_base::openmode mode = std::ios::out;
        if (Config->appendMode) {
            mode |= std::ios::app;  // Append if /A is specified
        }
        outputFileDataStream.open(Config->outputFileData, mode);
        if (!outputFileDataStream.is_open()) {
            std::cerr << "Error: Unable to open output file for the EMObs data export: " << Config->outputFileData << std::endl;
            return;
        }

        if (!Config->appendMode) {
            outputFileDataStream << L"Row\tPathEMObs\tFileEMObs\tOpCode\tRowType\tPeriod\tPath (Original Path, probably not valid now)\tFileLeft\tFileLeft Status\tFrameL\tPointLX1\tPointLY1\tPointLX2\tPointLY2\tFileRight\tFileRight Status\tFrameR\tPointRX1\tPointRY1\tLPointRX2\tPointRY2\tLength\tFamily\tGenus\tSpecies\tCount\n";  // Add your column headings here                
        }
    }

    // Open the EMObs TLC List export file if required
    if (Config->tlcMode && !Config->outputFileTLCList.empty()) {
        outputFileTLCListStream.open(Config->outputFileTLCList, std::ios::out | std::ios::trunc);
        if (!outputFileTLCListStream.is_open()) {
            std::cerr << "Error: Unable to open output file for the EMObs TLC List export: " << Config->outputFileTLCList << std::endl;
            return;
        }

        outputFileTLCListStream << L"Row\tPath\tFileName\tOffset\tTLC\tByte\tData1\tData2\tData3\n";  // Add your column headings here
    }

    // Open the EMObs TLC Hierarchy export file if required
    if (Config->tlcHierarchyMode && !Config->outputFileTLCHierarchy.empty()) {
        outputFileTLCHierarchyStream.open(Config->outputFileTLCHierarchy, std::ios::out | std::ios::trunc);
        if (!outputFileTLCHierarchyStream.is_open()) {
            std::cerr << "Error: Unable to open output file for the EMObs TLC List export: " << Config->outputFileTLCHierarchy << std::endl;
            return;
        }
        // No titles
    }

    // Open the EMObs Hex Dump
    if (Config->hexDumpMode && !Config->outputFileHexDump.empty()) {
        outputFileHexDumpStream.open(Config->outputFileHexDump, std::ios::out | std::ios::trunc);
        if (!outputFileHexDumpStream.is_open()) {
            std::cerr << "Error: Unable to open output file for the EMObs Hex Dump: " << Config->outputFileHexDump << std::endl;
            return;
        }
        // No titles
    }



    // Iterate through the directory (recursively if /s is specified)
    fs::directory_options dirOptions = fs::directory_options::skip_permission_denied;
    fs::directory_iterator endIter;  // End marker for iteration
    int ret = 0;
    std::list<struct _OutputRow*> outputRowsAdd;

    try {
       
        std::list<struct _OutputTLC*> outputTLCsAdd;

        if (Config->searchSubdirs) {
            for (const auto& entry : fs::recursive_directory_iterator(Config->searchPath, dirOptions)) {

                // Get the file or directory attributes
                DWORD attributes = GetFileAttributesW(entry.path().c_str());

                // Check if the entry is hidden or a system file, and skip it if so
                if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM))) {
                    continue;  // Skip hidden or system files/directories
                }

                if (entry.is_regular_file()) {
                    if (std::regex_match(entry.path().filename().string(), std::regex(fileSpec))) {
                        std::string foundFile = entry.path().string();
                        std::cout << "Found: " << foundFile << std::endl;

                        if (ret == 0 && Config->tlcMode == true)
                            ret = ExtractEMObsFileTLCs(foundFile, outputFileTLCListStream, outputTLCsAdd);

                        if (ret == 0 && Config->tlcHierarchyMode == true)
                            ret = ExtractEMObsFileTLCsDisplayHierarchy(foundFile, outputFileTLCHierarchyStream);

                        if (ret == 0 && Config->hexDumpMode == true)
                            ret = HexDumpEMObsFile(foundFile, outputFileHexDumpStream);

                        if (ret == 0 && Config->dataMode == true) {
                            // Open the EMObs file
                            EMObsReader reader(foundFile);

                            // Read the contains
                            ret = reader.Process(outputRowsAdd);
                        }
                    }
                }
            }
        }
        else {
            for (const auto& entry : fs::directory_iterator(Config->searchPath, dirOptions)) {

                // Get the file or directory attributes
                DWORD attributes = GetFileAttributesW(entry.path().c_str());

                // Check if the entry is hidden or a system file, and skip it if so
                if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM))) {
                    continue;  // Skip hidden or system files/directories
                }

                if (entry.is_regular_file()) {
                    if (std::regex_match(entry.path().filename().string(), std::regex(fileSpec))) {
                        std::string foundFile = entry.path().string();
                        std::cout << "Found: " << foundFile << std::endl;

                        if (ret == 0 && Config->tlcMode == true)
                            ret = ExtractEMObsFileTLCs(foundFile, outputFileTLCListStream, outputTLCsAdd);

                        if (ret == 0 && Config->tlcHierarchyMode == true)
                            ret = ExtractEMObsFileTLCsDisplayHierarchy(foundFile, outputFileTLCHierarchyStream);

                        if (ret == 0 && Config->hexDumpMode == true)
                            ret = HexDumpEMObsFile(foundFile, outputFileHexDumpStream);

                        if (ret == 0 && Config->dataMode == true) {
                            // Open the EMObs file
                            EMObsReader reader(foundFile);

                            // Read the contains
                            ret = reader.Process(outputRowsAdd);
                        }
                    }
                }
            }
        }
    }
    catch (const fs::filesystem_error& e) {
        std::cerr << "searchFiles() Filesystem error: " << e.what() << std::endl;
    }


    // Find all the .MP4 files within the same directory as the EMObs file
	std::wstring wsearchPath = std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>>().from_bytes(Config->searchPath);
    FileFind fileFind;

    ret = fileFind.ScanFiles(wsearchPath);

    if (ret == 0) {
        // Check for duplicate EMObs of the same name.  Often the data is delivered after the season with
        // EMOBs in the video directory and combined into a single EMObs directory. This needs to be resolved
		// to avoid duplicate entries in the output file.
        std::vector<FileItem> dupCheckEMBosList = fileFind.FindFirst(L"*.EMObs");
        while (!dupCheckEMBosList.empty()) {
			if (dupCheckEMBosList.size() > 1) {
				std::wcout << L"Error: Duplicate EMObs files found:" << std::endl;
				for (FileItem fileItem : dupCheckEMBosList) {
					std::wcout << L"    [" << fileItem.fileSpec << L" Size: " << fileItem.fileSize << "]" << std::endl;
				}
				std::wcout << std::endl;
			}

            dupCheckEMBosList = fileFind.FindNext();
        }


		// Iterate through the list of output rows
        for (struct _OutputRow* item : outputRowsAdd) {
            std::vector<FileItem> itemsLeft;
            std::vector<FileItem> itemsRight;
            std::wstring searchFileL;
            std::wstring searchFileR;

            //???std::wstring test = L"AD_10_1_2017_07_14_Left.avi";
            //???if (_wcsicmp(item->FileL.c_str(), test.c_str()) == 0)
            //???    test = L"";

            // Check of the file needed to be remapping to a different name
			searchFileL = fileMapping.findNewFile(item->FileL);
            if (searchFileL.empty())
                searchFileL = item->FileL;
            searchFileR = fileMapping.findNewFile(item->FileR);
            if (searchFileR.empty())
                searchFileR = item->FileR;

            if (!item->FileL.empty())
                itemsLeft = fileFind.getFileInfo(searchFileL);
            if (!item->FileR.empty())
                itemsRight = fileFind.getFileInfo(searchFileR);


            switch (item->rowType) {

            case RowType::MeasurementPoint3D:
			case RowType::Point3D:
                // This is the simple case. Check there is only one left and right file found 
                if (itemsLeft.size() == 1 && itemsRight.size() == 1) {
                    fs::path pathLeft(itemsLeft[0].fileSpec);
                    fs::path pathRight(itemsRight[0].fileSpec);

                    // Both the left and right MP4 need to be in the same directory
                    if (pathLeft.parent_path().wstring() == pathRight.parent_path().wstring()) {
                        item->Path = pathLeft.parent_path().wstring();
                        item->FileL = pathLeft.filename().wstring();
                        item->FileLStatus = L"Ok";
                        item->FileR = pathRight.filename().wstring();
                        item->FileRStatus = L"Ok";
                    }
                    else {
                        std::wcout << L"Error Row:" << item->row << "  " << RowTypeToString(item->rowType) << " Left and right MP4 files are not in the same directory" << std::endl;
                        item->FileLStatus = L"Path differ:" + pathLeft.parent_path().wstring();
                        item->FileRStatus = L"Path differ:" + pathRight.parent_path().wstring();
                        item->Path = L"";
                    }
                }
                else if (itemsLeft.size() == 0 && itemsRight.size() == 0) {
                    std::wcout << L"Error Row:" << item->row << "  " << RowTypeToString(item->rowType) << " Left and right MP4 file not found, left:[" << searchFileL << "], right:[" << searchFileR << "]" << std::endl;
                    item->FileLStatus = L"Missing";
                    item->FileRStatus = L"Missing";
                    item->Path = L"";
                }
                else if (itemsLeft.size() == 0) {
                    std::wcout << L"Error Row:" << item->row << "  " << RowTypeToString(item->rowType) << " Left MP4 file not found, left:[" << searchFileL << "]" << std::endl;
                    item->FileLStatus = L"Missing";
                    item->FileRStatus = L"Found:" + std::to_wstring(itemsRight.size());
                    item->Path = L"";
                }
                else if (itemsRight.size() == 0) {
                    std::wcout << L"Error Row:" << item->row << "  " << RowTypeToString(item->rowType) << " Right MP4 file not found, right:[" << searchFileR << "]" << std::endl;
                    item->FileLStatus = L"Found:" + std::to_wstring(itemsLeft.size());
                    item->FileRStatus = L"Missing";
                    item->Path = L"";
                }
                else {
                    std::wcout << L"Error Row:" << item->row << " " << RowTypeToString(item->rowType) << " Left file count=" << itemsLeft.size() << ", Right file count=" << itemsRight.size();
                    if (itemsLeft.size() > 0) {
                        std::wcout << L", Left files:";
                        for (FileItem fileItem : itemsLeft) {
                            std::wcout << L"[" << fileItem.fileSpec << L" Size: " << fileItem.fileSize << "],";
                        }
                        std::wcout << std::endl;
                    }
                    if (itemsRight.size() > 0) {
                        std::wcout << L", Right files:";
                        for (FileItem fileItem : itemsRight) {
                            std::wcout << L"[" << fileItem.fileSpec << L" Size: " << fileItem.fileSize << "],";
                        }
                        std::wcout << std::endl;
                    }
                    item->FileLStatus = L"Found:" + std::to_wstring(itemsLeft.size());
                    item->FileRStatus = L"Found:" + std::to_wstring(itemsRight.size());
                    item->Path = L"";
                }            
                break;

            case RowType::Point2DLeftCamera:
                if (itemsLeft.size() == 1) {
                    fs::path pathLeft(itemsLeft[0].fileSpec);

                    item->Path = pathLeft.parent_path().wstring();
                    item->FileL = pathLeft.filename().wstring();
                    item->FileLStatus = L"Ok";
                    item->FileRStatus = L"";
                }
                else if (itemsLeft.size() == 0) {
                    item->FileLStatus = L"Missing";
                    item->FileRStatus = L"";
                    item->Path = L"";
                }
                else {
                    std::wcout << L"Error Row:" << item->row << " " << RowTypeToString(item->rowType) << " Left file count=" << itemsLeft.size();
                    if (itemsLeft.size() > 0) {
                        std::wcout << L", Left files:";
                        for (FileItem fileItem : itemsLeft) {
                            std::wcout << L"[" << fileItem.fileSpec << L" Size: " << fileItem.fileSize << "],";
                        }
                        std::wcout << std::endl;
                    }
                    item->FileLStatus = L"Found:" + std::to_wstring(itemsLeft.size());
					item->FileRStatus = L"";
                    item->Path = L"";
                }
                break;

            case RowType::Point2DRightCamera:
                if (itemsRight.size() == 1) {
                    fs::path pathRight(itemsRight[0].fileSpec);

                    item->Path = pathRight.parent_path().wstring();
                    item->FileR = pathRight.filename().wstring();
                    item->FileRStatus = L"Ok";
                    item->FileLStatus = L"";
                }
                else if (itemsRight.size() == 0) {
                    item->FileRStatus = L"Missing";
                    item->FileLStatus = L"";
                    item->Path = L"";
                }
                else {                    
                    std::wcout << L"Error Row:" << item->row << " " << RowTypeToString(item->rowType) << " Right file count = " << itemsRight.size();
                    if (itemsRight.size() > 0) {
                        std::wcout << L", Right files:";
                        for (FileItem fileItem : itemsRight) {
                            std::wcout << L"[" << fileItem.fileSpec << L" Size: " << fileItem.fileSize << "],";
                        }
                        std::wcout << std::endl;
                    }
                    item->FileRStatus = L"Found:" + std::to_wstring(itemsRight.size());
                    item->FileLStatus = L"";
                    item->Path = L"";
                }
                break;
            }
        }
    }

    if (ret == 0) {
        if (outputFileDataStream.is_open()) {

            std::wstring rowToWrite;

            for (struct _OutputRow* item : outputRowsAdd) {
                std::wstring opCodeItem = ReplaceTabs(item->opCode);
                std::wstring PeriodItem = ReplaceTabs(item->Period);
                std::wstring FamilyItem = ReplaceTabs(item->Family);
                std::wstring GenusItem = ReplaceTabs(item->Genus);
                std::wstring SpeciesItem = ReplaceTabs(item->Species);

                // Concatenate each field with a tab delimiter
                std::wstringstream ss;
                ss << item->row << L"\t";
                ss << item->PathEMObs << L"\t";
                ss << item->FileEMObs << L"\t";
                ss << opCodeItem << L"\t";
                ss << RowTypeToString(item->rowType) << L"\t";
                ss << PeriodItem << L"\t";
                ss << item->Path << L"\t";
                ss << item->FileL << L"\t";
				ss << item->FileLStatus << L"\t";
                ss << item->FrameL << L"\t";
                ss << item->PointLX1 << L"\t";
                ss << item->PointLY1 << L"\t";
                ss << item->PointLX2 << L"\t";
                ss << item->PointLY2 << L"\t";
                ss << item->FileR << L"\t";
                ss << item->FileRStatus << L"\t";
                ss << item->FrameR << L"\t";
                ss << item->PointRX1 << L"\t";
                ss << item->PointRY1 << L"\t";
                ss << item->PointRX2 << L"\t";
                ss << item->PointRY2 << L"\t";
                ss << item->Length << L"\t";
                ss << FamilyItem << L"\t";
                ss << GenusItem << L"\t";
                ss << SpeciesItem << L"\t";
                ss << item->count;

                // Write the row to the output file
                rowToWrite = ss.str();
                outputFileDataStream << rowToWrite << std::endl;
            }

            // Clear the list of output rows
            for (struct _OutputRow* item : outputRowsAdd) {
                delete item;
            }
            outputRowsAdd.clear();
        }
    }


    if (outputFileDataStream.is_open()) 
        outputFileDataStream.close();
    if (outputFileTLCListStream.is_open())
		outputFileTLCListStream.close();
    if (outputFileTLCHierarchyStream.is_open())
		outputFileTLCHierarchyStream.close();
    if (outputFileHexDumpStream.is_open())
		outputFileHexDumpStream.close();
}



// Static function to replace tab characters with "<Tab>"
std::wstring ReplaceTabs(const std::wstring& input) {
    std::wstring output = input;  // Copy the input string to modify
    std::wstring tabReplacement = L"<Tab>";  // Define the replacement string

    size_t pos = 0;
    // Find and replace all occurrences of tab characters
    while ((pos = output.find(L'\t', pos)) != std::wstring::npos) {
        output.replace(pos, 1, tabReplacement);  // Replace '\t' with "<Tab>"
        pos += tabReplacement.length();  // Move past the replacement
    }

    return output;  // Return the modified string
}


// Function to convert RowType to a string representation
std::wstring RowTypeToString(RowType type) {
    switch (type) {
    case RowType::MeasurementPoint3D:
        return L"3D Measurement";
    case RowType::Point3D:
        return L"3DPoint";
    case RowType::Point2DLeftCamera:
        return L"2DPoint Left";
    case RowType::Point2DRightCamera:
        return L"2DPoint Right";
    default:
        return L"Unknown";
    }
}


int ExtractEMObsFileTLCs(const std::string foundFile, std::wofstream& outputFileStream, std::list<struct _OutputTLC*>& outputTLCsAdd) {
    
    int ret = 0;

    // Open the EMObs file
    EMObsReader reader(foundFile);

    // Read the contains
    ret = reader.ExtractTLCs(outputTLCsAdd);

    if (ret == 0) {
        if (outputFileStream.is_open()) {

            std::wstring rowToWrite;

            for (struct _OutputTLC* item : outputTLCsAdd) {

                // Concatenate each field with a tab delimiter
                std::wstringstream ss;
                ss << item->row << L"\t";
                ss << item->Path << L"\t";
                ss << item->File1 << L"\t"; 
                ss << std::setfill(L'0') << std::setw(8) << std::hex << std::uppercase << item->seekOffset << L"\t";
                ss << item->tlc << L"\t";
                ss << (int)item->cTLCByte << L"\t";
                ss << item->data1 << L"\t";
                ss << item->data2 << L"\t";
                ss << item->data3 << L"\t";

                // Write the row to the output file
                outputFileStream << ss.str() << std::endl;
            }

            // Clear the list of output rows
            for (struct _OutputTLC* item : outputTLCsAdd) {
                delete item;
            }
            outputTLCsAdd.clear();
        }
    }

    return ret;
}


int ExtractEMObsFileTLCsDisplayHierarchy(const std::string foundFile, std::wofstream& outputFileStream) {

    int ret = 0;

    std::list<struct _OutputTLC*>outputTLCsAdd;

    // Open the EMObs file
    EMObsReader reader(foundFile);

    // Read the contains
    ret = reader.ExtractTLCs(outputTLCsAdd);

    if (ret == 0) {
        if (outputFileStream.is_open()) {

            // Convert std::string to std::wstring
            std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> converter;
            std::wstring wfileSpec = converter.from_bytes(foundFile);
            outputFileStream << std::endl << wfileSpec << std::endl;


			int currentLevel = 0;
            std::wstring rowToWrite;
            std::wstringstream ss;
            std::wstring indent;

            for (struct _OutputTLC* item : outputTLCsAdd) {

                if (item->tlc == L"EBS" || item->tlc == L"IDA" || item->tlc == L"CCC" || item->tlc == L"CMS" || item->tlc == L"PER") {
                    currentLevel = 1;

                    if (item->tlc != L"EBS")
                        ss << L"\n";

                    std::wstring spaces(3, L' ');  // 10 spaces
                    indent = spaces;


                    // Format the number as hexadecimal, with padding of zeros, 8 digits long
                    ss << std::setfill(L'0') << std::setw(8) << std::hex << std::uppercase << item->seekOffset << indent;

                }
                else if (item->tlc == L"PDA" || item->tlc == L"PDL" || item->tlc == L"PD3") {
                    currentLevel = 2;

                    std::wstring spaces(18, L' ');  // 10 spaces
                    indent = spaces;
                    ss << std::endl << indent;
                }
               
                ss << item->tlc << (int)item->cTLCByte << ">";
            }

            // Write the row to the output file
            outputFileStream << ss.str();


            // Clear the list of output rows
            for (struct _OutputTLC* item : outputTLCsAdd) {
                delete item;
            }
            outputTLCsAdd.clear();

            ss << std::endl;
        }
    }

    return ret;
}

/*
Seem TLC and TLC counts From Utila 2023
CAM	110
CCC	110
CIN	55
CMS	110
CPT	5474
EBS	55
FRA	3130
IDA	1415
MAT	3090
MSI	112
PD3	986
PDA	978
PDL	631
PED	49
PER	55
PTN	55*/


int HexDumpEMObsFile(const std::string foundFile, std::wofstream& outputFileStream) {

    int ret = 0;

    // Open the EMObs file
    EMObsReader reader(foundFile);

    if (outputFileStream.is_open()) {
        // Get the file size
        std::filesystem::path path(foundFile);
        uintmax_t fileSize = std::filesystem::file_size(path);
		uintmax_t pages = fileSize / (48*16);


        // Convert std::string to std::wstring
        std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> converter;
        std::wstring wfileSpec = converter.from_bytes(foundFile);
        outputFileStream << std::endl << wfileSpec << L"  Size: " << fileSize << " bytes  Pages:" << pages << std::endl;

        reader.HexDumpToFile(outputFileStream, 16/*row width*/, 48/*row per page*/);
    }


    return ret;
}
