#pragma once

#include <iostream>
#include <fstream>
#include <string>
#include <unordered_map>
#include <sstream>
#include <algorithm>
#include <cwctype>  // for towupper

#include "FileMapping.h"

// Implementation

FileMapping::FileMapping(const std::string& filePath) {
    std::wifstream file(filePath);

    if (!file.is_open()) {
        std::cerr << "Error opening file: " << filePath << std::endl;
        return;
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

    if (std::getline(iss, oldFile, L'\t') && std::getline(iss, newFile, L'\t')) {
        return { oldFile, newFile };
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
