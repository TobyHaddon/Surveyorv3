#pragma once

#include <iostream>
#include <fstream>
#include <string>
#include <unordered_map>
#include <sstream>
#include <algorithm>
#include <cwctype>  // for towupper
#include <filesystem>

#include "FileMapping.h"

// Implementation

FileMapping::FileMapping(const std::string& filePath) {

	std::filesystem::path relativePath = std::filesystem::path(filePath);
    std::filesystem::path absolutePath = std::filesystem::absolute(relativePath);

    std::wifstream file(absolutePath);

    if (!file.is_open()) {
        std::cerr << "Error opening file mapping file: " << filePath << std::endl;
		std::cerr << "  The File Mapping file can be used to adjust file name found in the EMObs to a new file name." << std::endl;
        std::cerr << "  This can be useful if a movie file was renamed after the .EMObs was created and the file names no longer correspond." << std::endl;
        std::cerr << "  The File Mapping file format is a simple tab delimitered list of file name from and file name to." << std::endl;
        std::cerr << "  Example of the format:" << std::endl;
        std::cerr << "AD_10_1_2017_07_14_Left.avi\tAD_10_1_2017_07_14_Left.MP4" << std::endl;
        std::cerr << "AD_10_1_2017_07_14_Right.avi\tAD_10_1_2017_07_14_Right.MP4" << std::endl;
        std::cout << "Press Enter to continue...\n";
        // Wait for Enter to be pressed
        std::string input;
        std::getline(std::cin, input);
    }

    std::wstring line;
    while (std::getline(file, line)) {
        auto [oldFile, newFile] = parseLine(line);

        std::transform(oldFile.begin(), oldFile.end(), oldFile.begin(), towupper);
        std::transform(newFile.begin(), newFile.end(), newFile.begin(), towupper);

        if (!oldFile.empty() && !newFile.empty()) {
            fileMap[oldFile] = newFile;
        }
    }

    file.close();
}

// Function to parse a line, splitting by the tab character
std::pair<std::wstring, std::wstring> FileMapping::parseLine(const std::wstring& line) const {
    std::wistringstream iss(line);
    std::wstring oldFile, newFile;

    // Debug: Output the line being parsed
    std::wcout << L"Parsing line: \"" << line << L"\"" << std::endl;

    if (std::getline(iss, oldFile, L'\t')) {
        std::wcout << L"First part: \"" << oldFile << L"\"" << std::endl;

        if (std::getline(iss, newFile, L'\t')) {
            std::wcout << L"Second part: \"" << newFile << L"\"" << std::endl;
            return { oldFile, newFile };
        }
        else {
            std::wcout << L"Failed to get second part." << std::endl;
        }
    }
    else {
        std::wcout << L"Failed to get first part." << std::endl;
    }

    return { L"", L"" };  // Return empty pair if parsing fails
}

// Function to find the new file corresponding to the old file spec
std::wstring FileMapping::findNewFile(const std::wstring& findFileName) const {

    std::wstring findFileNameUpper = findFileName;
    std::transform(findFileNameUpper.begin(), findFileNameUpper.end(), findFileNameUpper.begin(), towupper);

    auto it = fileMap.find(findFileNameUpper);
    if (it != fileMap.end()) {
        return it->second;  // Return the new file
    }
    return {};  // Return empty string if not found
}
