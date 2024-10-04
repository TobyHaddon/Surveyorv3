#pragma once
#include <filesystem>
#include <string>
#include <vector>
#include <unordered_map>
#include <regex>
#include <iostream>


struct FileItem {
    std::wstring fileSpec;
    uintmax_t fileSize;
};

class FileFind {
public:
    FileFind();
    ~FileFind();

    int ScanFiles(const std::wstring& searchPath);

    // New member functions for finding files with a filter
    std::vector<FileItem> FindFirst(const std::wstring& searchFilter);
    std::vector<FileItem> FindNext();

    std::vector<FileItem> getFileInfo(const std::wstring& fileName);

private:
    std::unordered_map<std::wstring, std::vector<FileItem>> fileDictionary;

    // For FindFirst/FindNext functionality
    std::unordered_map<std::wstring, std::vector<FileItem>>::iterator currentIterator;
    std::unordered_map<std::wstring, std::vector<FileItem>>::iterator endIterator;
    std::wregex currentFilter;
    bool searchStarted;
};
