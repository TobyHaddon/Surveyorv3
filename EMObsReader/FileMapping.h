#pragma once


class FileMapping {
public:
    // Constructor that takes the file path as input and reads the file
    FileMapping(const std::string& filePath);

    // Function to check if a file spec is in the left column and return the corresponding right column value
    std::wstring findNewFile(const std::wstring& oldFileSpec) const;

private:
    // Unordered map to store old file as key and new file as value
    std::unordered_map<std::wstring, std::wstring> fileMap;

    // Helper function to split a line by a tab character
    std::pair<std::wstring, std::wstring> parseLine(const std::wstring& line) const;
};
