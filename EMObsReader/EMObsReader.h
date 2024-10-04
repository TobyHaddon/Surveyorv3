#pragma once



// Output Structure
enum RowType {
    None,
    MeasurementPoint3D,
    Point3DLeftCamera,
    Point3DRightCamera,
    Point2DLeftCamera,
    Point2DRightCamera
};

struct _OutputRow {
    int row;
    std::wstring PathEMObs;
    std::wstring FileEMObs;
    std::wstring opCode;
    RowType rowType;
    std::wstring Period;
    std::wstring Path;
    std::wstring FileL;
    std::wstring FileLStatus;
    long FrameL;
    double PointLX1;
    double PointLY1;
    double PointLX2;
    double PointLY2;
    std::wstring FileR;
    std::wstring FileRStatus;
    long FrameR;
    double PointRX1;
    double PointRY1;
    double PointRX2;
    double PointRY2;
    double Length;
    std::wstring Family;
    std::wstring Genus;
    std::wstring Species;
    int count;
};


struct _OutputTLC {
    int row;
    std::wstring Path;
    std::wstring File1;
    long seekOffset;
    std::wstring tlc;
    char cTLCByte;
    std::wstring data1;     // Used for development work and using the structure of the EMObs
    std::wstring data2;     // Used for development work and using the structure of the EMObs
    std::wstring data3;     // Used for development work and using the structure of the EMObs
};


class EMObsReaderBase {

private:
    std::string& filespec;
    unsigned char* readBuffer;
    size_t readBufferSize = 0;

    // GetNext type current pointer
    long seekPointer = 0;
    long readPointer = 0;
	long lastTLCSeekPointer = 0;

    // FindFirst/Next wstring
    unsigned char* p = nullptr;
    int size = 0;
    unsigned char* pLast = nullptr;
    

public:
    EMObsReaderBase(std::string& _fileSpec);
    ~EMObsReaderBase();

    // Open, read and close the file
    int ReadFile();
    size_t GetSize();


    // Get basic types
    int PeekNextTLC(char* TLC);
    //std::string GetNextAsString();
    long GetReadPointer();
    std::wstring GetNextAsWString();
    std::int64_t GetNextAsInt64();
    std::int32_t GetNextAsInt32();
    std::int16_t GetNextAsInt16();
    char* GetNextAsFixedChar(char* buffer, size_t len);
    float GetNextAsFloat();
    double GetNextAsDouble();

    // complex types
    std::vector<std::vector<std::wstring>> GetNextAsMAT();

    // Find any TLCs
    int GetFirstTLC(void** p, int* size, char* TLC);
    int GetNextTLC(void** p, int* size, char* TLC);    
	long GetLastTLCSeekPointer();
    void SetSeekPointerToReadPointer();
    void SetReadPointerToSeekPointer();
    void SetReadPointerToLastTLCSeekPointer();
  
    // Find any wstrings
    long FindFirstwstring(void* p, int size, int* wssize);
    long FindNextwstring(int* wssize);

    // Hex Dump
	int HexDumpLine(long seek, int dataLength, int widthToDisplay, std::wstring& address, std::wstring& hex, std::wstring& asc);


private:
    static long getFileSize(const std::string& fileName);
    static bool readFileIntoBuffer(const std::string& fileName, unsigned char* buffer, size_t bufferSize);

    long findNextTLC(long startPointer, char* TLC);
    bool IsTLC(long startPointer, char* TLC);

};

class EMObsReader {

private:
    std::string filespec;
    EMObsReaderBase* reader;

public:
    EMObsReader(const std::string& _filespec);
    
    int Process(std::list<struct _OutputRow*>& outputRowsAdd);
    int ExtractTLCs(std::list<struct _OutputTLC*>& outputTLCsAdd);
    int HexDumpToFile(std::wofstream& outputFileStream, int rowWidth, int rowsPerPage);

private:

    struct _EBS* GetEBS();
    struct _CIN* GetCIN();
    struct _PTN* GetPTN();
    struct _IDA* GetIDA();
    struct _FRA* GetFRA();
    struct _PDA* GetPDA();
    struct _PDL* GetPDL();
    struct _PD3* GetPD3();
    struct _CPT* GetCPT();
    struct _CMS* GetCMS();
    struct _PER* GetPER();
    struct _CCC* GetCCC();

    
};
