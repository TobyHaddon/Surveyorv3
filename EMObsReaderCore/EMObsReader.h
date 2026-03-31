#pragma once


// Output Structure
enum RowType {
    None,
    MeasurementPoint3D,
    Point3D,
    Point2DLeftCamera,
    Point2DRightCamera
};

struct _SurveyRow {
    int row;
    std::wstring PathEMObs;
    std::wstring FileEMObs;
    std::wstring opCode;
    std::wstring Analyst;
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

struct _OutputPeriodRow {
    int row;
    std::wstring PeriodName;
    int Camera;                 // 0 = Left, 1 = Right
    std::wstring MediaFile;
    std::int32_t StartFrame;
    std::int32_t EndFrame;
};

struct _OutputMediaInfoRow
{
    int row;
	bool TrueLeftFalseRightCamera;  
    std::wstring MediaFile;
    std::int32_t FrameCount;
    double FrameRate;
};

struct _OutputCalibrationRow
{
    int row;
    bool TrueLeftFalseRightCamera;
    std::wstring CameraName;
    std::wstring DerivedFrom;
    double XPixelSize;
    double YPixelSize;
    std::int32_t FrameHeight;
    std::int32_t FrameWidth;
    double XPPOffset;
    double YPPOffset;
    double FocalLength;
    double K3RadialDistortion;
    double K5RadialDistortion;
    double K7RadialDistortion;
    double P1DecenteringDistortion;
    double P2DecenteringDistortion;
    double Orthogonality;
    double Affinity;
    double CameraX;
    double CameraY;
    double CameraZ;
    double Omega;
    double Phi;
    double Kappa;
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

struct _TLCOffset
{
    std::wstring tlc;
    long seekOffset;
    long size;
	int level;  // zero if top level, 1 if nested in a top level TLC, etc.
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
	std::string& GetFileSpec() const { return filespec; }

    // Get basic types
    int PeekNextTLC(char* TLC);
    //std::string GetNextAsString();
    long GetReadPointer() const;
    void* GetReadPointerPtr(long pointer);
    std::wstring GetNextAsWString();
    std::int64_t GetNextAsInt64();
    std::int32_t GetNextAsInt32();
    std::int16_t GetNextAsInt16();
    char* GetNextAsFixedChar(char* buffer, size_t len);
    unsigned char* GetNextAsFixedUChar(unsigned char* buffer, size_t len);
    float GetNextAsFloat();
    double GetNextAsDouble();
    unsigned char GetNextAsUnsignedChar();

    // Complex types
    std::vector<std::vector<std::wstring>> GetNextAsMATwstring();
    std::vector<std::vector<double>> GetNextAsMATdouble();
    std::vector<std::vector<unsigned char>> GetNextAsMATbyte();
    template <typename TOwner, typename T>
    std::list<T*> GetArray(size_t count, TOwner* owner, T* (TOwner::* getter)());

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

    std::list<struct _CMS*> CMSList;    // List of 2 items max
    struct _PER* pPER = nullptr;
    std::list<struct _CCC*> CCCList;    // List of 2 items max

    std::list<struct _TLCOffset*> TLCOffsetList;    // List of TLC offsets

public:
    EMObsReader(const std::string& _filespec);
    int Clear();
    int Process(std::list<struct _SurveyRow*>& outputRows);
    int ExtractTLCs(std::list<struct _OutputTLC*>& outputTLCs);
    void GetTLCOffsetList(std::list<struct _TLCOffset*>& outputTLCOffsets) const;
    void GetPeriodRows(std::list<struct _OutputPeriodRow*>& outputPeriodRows) const;
    void GetMediaInfoRows(std::list<struct _OutputMediaInfoRow*>& outputRows) const;
    void GetCalibrationRows(std::list<struct _OutputCalibrationRow*>& outputRows) const;
    int HexDumpToFile(std::wofstream& outputFileStream, int rowWidth, int rowsPerPage);
    
private:

    void LogTLCOffset(struct _TLC* pTLC, int level); //zero if top level

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
    struct _MSI* GetMSI();
    struct _PER* GetPER();    
    struct _PED* GetPED();
    struct _CCC* GetCCC();
	struct _CAM* GetCAM();
};
