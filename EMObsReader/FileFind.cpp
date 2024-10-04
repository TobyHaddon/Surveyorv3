#include <windows.h>  // For GetFileAttributesW and file attribute constants
#include "FileFind.h"

namespace fs = std::filesystem;

FileFind::FileFind() : searchStarted(false), currentIterator(endIterator) {}

FileFind::~FileFind() {}

int FileFind::ScanFiles(const std::wstring& searchPath) {
    int ret = 0;

    fs::path path(searchPath);
    if (!fs::exists(path)) {
        std::wcout << L"Path does not exist: " << searchPath << std::endl;
        return -2;
    }

    try {
        for (const auto& entry : fs::recursive_directory_iterator(searchPath, fs::directory_options::skip_permission_denied)) {
            try {
                // Get the file or directory attributes
                DWORD attributes = GetFileAttributesW(entry.path().c_str());

                // Check if the entry is hidden or a system file, and skip it if so
                if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & (FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM))) {
                    continue;  // Skip hidden or system files/directories
                }

                // Process regular files
                if (fs::is_regular_file(entry.path())) {
                    std::wstring fileName = entry.path().filename().wstring();
                    std::wstring fullPath = entry.path().wstring();
                    uintmax_t fileSize = fs::file_size(entry.path());

                    // Convert the filename to uppercase
                    std::transform(fileName.begin(), fileName.end(), fileName.begin(), towupper);

                    // Insert file information into the dictionary
                    fileDictionary[fileName].push_back({ fullPath, fileSize });
                }
            }
            catch (const fs::filesystem_error& ex) {
                // Handle errors for specific files and skip over them
                std::wcout << L"Error processing file: " << entry.path().wstring() << L" - " << ex.what() << std::endl;
                continue;
            }
        }
    }
    catch (const fs::filesystem_error& ex) {
        std::wcout << L"FileFind::ScanFiles() File system error: " << ex.what() << std::endl;
        ret = -1;
    }

    return ret;
}


std::vector<FileItem> FileFind::getFileInfo(const std::wstring& fileName) {

    std::wstring fileNameUpper = fileName;
    std::transform(fileNameUpper.begin(), fileNameUpper.end(), fileNameUpper.begin(), towupper);

    auto it = fileDictionary.find(fileNameUpper);
    if (it != fileDictionary.end()) {
        return it->second;
    }
    return {};
}

// Helper function to convert a wildcard (like "*.EMObs") to a regex pattern
std::wstring WildcardToRegex(const std::wstring& wildcard) {
    std::wstring regexPattern = L"^" + std::regex_replace(wildcard, std::wregex(L"\\*"), L"(.*)") + L"$";
    return regexPattern;
}

std::vector<FileItem> FileFind::FindFirst(const std::wstring& searchFilter) {

    std::vector<FileItem> matchingFiles;

    // Convert the search filter (e.g., "*.EMObs") to a regex pattern
    std::wstring regexPattern = WildcardToRegex(searchFilter);
    currentFilter = std::wregex(regexPattern, std::regex_constants::icase);  // Case-insensitive search
    searchStarted = true;

    // Start iterating over the fileDictionary from the beginning
    currentIterator = fileDictionary.begin();
    endIterator = fileDictionary.end();

    // Find the first matching file
    for (; currentIterator != endIterator; ++currentIterator) {
        if (std::regex_match(currentIterator->first, currentFilter)) {
            matchingFiles = currentIterator->second;  // Get the matching file information
            ++currentIterator;  // Move the iterator forward for the next search
            break;
        }
    }

    return matchingFiles;
}

std::vector<FileItem> FileFind::FindNext() {
    std::vector<FileItem> matchingFiles;

    if (!searchStarted || currentIterator == endIterator) {
        return matchingFiles;
    }

    // Find the next matching file
    for (; currentIterator != endIterator; ++currentIterator) {
        if (std::regex_match(currentIterator->first, currentFilter)) {
            matchingFiles = currentIterator->second;  // Get the matching file information
            ++currentIterator;  // Move the iterator forward for the next search
            break;
        }
    }

    return matchingFiles;
}
