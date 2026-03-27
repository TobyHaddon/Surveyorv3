// EMObsReaderCore.cpp : Defines the functions for the static library.
//
// Version 1.1 10 Sep 2024  Fixed bug with PD3 putting the right camera data in the left camera fields X2,Y2 fields

#include "pch.h"
#include "framework.h"

#include "EMObsReader.h"

namespace fs = std::filesystem;

//#pragma pack(push, 1) // Save the current alignment setting and set alignment to 1 byte

// EBS and children
struct _EBS {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;                   // Seen 4 and 5 but no change in the data
    std::wstring wsPictureDirectory;

    struct _CIN* pCIN;                  // Holds the opcode data
    struct _PTN* pPTN;                  // Holds the input field titles
};
struct _CIN {   // Holds the opcode data
    long fileSeekPointer;
    char cTLC[3]{};
    char cTLCVersion;

    std::vector<std::vector<std::wstring>> matTitle;
    std::vector<std::vector<std::wstring>> matValue;
};
struct _PTN {   // Holds the input field titles
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;
    std::vector<std::vector<std::wstring>> matCollectionHeadings;
    int32_t iData1;	    // seen as 86  
};

// IDA and children
// IDA is used to hold arrays of 3D Measurement Points (PDL), 3D Points (PD3) and Points (PDA)
// The character after this TLC is always ASCII 0x05
// This is a variable length structure 
// If always starts with an FRA

struct _IDA {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    struct _FRA* pFRA;

    // 2D Point Array
    struct {
        int32_t iPDACount;
        std::list<struct _PDA*> PDAList;

    } TypePDA;

#pragma pack(push, 1)
    union {
        char bData[16];
        int32_t ints[4];
        double doubles[2];
    } data1;
#pragma pack(pop)

    std::wstring wsPeriodName;

    // 3D Measurement Point Array
    struct {
        int32_t iPDLCount;
        std::list<struct _PDL*> PDLList;
    } TypePDL;

    // 3D Point Array
    struct {
        int32_t iPD3Count;
        std::list<struct _PD3*> PD3List;
    } TypePD3;

#pragma pack(push, 1)
    union {
        char bData[16];
        int32_t ints[4];
        double doubles[2];
    } data2;
#pragma pack(pop)
};

// FRA is used to hold a left/right camera indicator, a frame number and a media file (MP4)
// The character after this TLC is always ASCII 0x01
// This is a variable length structure 
struct _FRA {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    int32_t iCameraZeroLeftOneRight;
    int32_t iFrameIndex;
    std::wstring wsMediaFile;
};

// PDA is used to hold a single 2D Point in a left or right camera frame
// The character after this TLC is always ASCII 0x01
// This is a variable length structure 
// PDL is exclusively a child of IDA
struct _PDA {       // Believe to indicate a Point in a frame
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;    // Seen 0 and 1, 1 has an additional 16 bytes of unknown data are the MAT

    struct _CPT* pCPT;
    std::vector<std::vector<std::wstring>> matCollectionValues;

    char bData[16];		 // Not used in version 0
};

// CPT is used to hold an X,Y position on a frame
// The character after this TLC is always ASCII 0x00
// This is a fixed length structure of 16 bytes (2x double)
struct _CPT {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    double X;
    double Y;
};


// PDL is used to hold a 3D measurement point i.e. 2x3D points in the left camera frame and 2x3D points in the right camera frame
// The character after this TLC is always ASCII 0x01
// This is a variable length structure 
// PDL is exclusively a child of IDA
struct _PDL {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    int32_t iData1;	    // seen 2
    struct _CPT* pCPT1;
    struct _CPT* pCPT2;
    int32_t iData2;	    // seen 2  
    struct _CPT* pCPT3;
    struct _CPT* pCPT4;
    struct _FRA* pFRA;
    std::vector<std::vector<std::wstring>> matCollectionValues;
};

// PD3 is used to hold a single 3D Point in a left or right camera frame
// The character after this TLC is always ASCII 0x00 (different to PDA and PDL)
// This is a variable length structure 
// PD3 is exclusively a child of IDA
struct _PD3 {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    struct _CPT* pCPT1;
    struct _CPT* pCPT2;
    struct _FRA* pFRA;
    std::vector<std::vector<std::wstring>> matCollectionValues;
};


// CMS and children
struct _CMS {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    int Count;
    std::vector<struct _MSI*> pMSIArray;
};

// MSI
struct _MSI {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    std::wstring wsMediaFile;
	int64_t FrameCount;
    double FrameRate;
};


// PER and children
struct _PER {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    int PeriodCount;
    std::vector<struct _PED*> pPEDArray;
};

// PED and children
struct _PED {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    std::wstring wsPeriodName;	
    char bData[17];
    struct _FRA* pFRAStart;
    struct _FRA* pFRAEnd;
};


// CCC and children
struct _CCC {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;       // seen 0

    char bData1[5];          // seen 00 01 00 00 00
    struct _CAM* pCAM;
    char bData2[8];		 
    long frameHeight;               // pixels
    long frameWidth;                // pixels

};

// CAM and children
struct _CAM {
    long fileSeekPointer;
    char cTLC[3];
    char cTLCVersion;

    std::wstring wsCameraName;
    std::wstring wsDerivedFrom;

    double xPixelSize;              // mm
	double yPixelSize;              // mm
    long frameHeight;               // pixels
	long frameWidth;                // pixels
    
    double xPPOffset;               // mm
    double yPPOffset;               // mm
	double focalLength;             // mm
	double k3RadialDistortion;      // mm^-2
	double k5RadialDistortion;      // mm^-4
	double k7RadialDistortion;      // mm^-6
	double p1DecenteringDistortion; // mm^-1
	double p2DecenteringDistortion; // mm^-1
	double orthogonality;           // dimensionless
	double affinity;                // dimensionless
	double cameraX;                 // mm
	double cameraY;                 // mm
	double cameraZ;                 // mm
    double omega;                   // degrees
    double phi;                     // degrees
	double kappa;                   // degrees
};

//#pragma pack(pop) // Restore the previous alignment setting



static void hexDump(const char* desc, long seekOffset, void* data, int len);
static void DisplayEBS(struct _EBS* pEBS);
static void DisplayIDA(struct _IDA* pIDA);
static void DisplayCMS(struct _CMS* pCMS);
static void DisplayMSI(struct _MSI* pMSI);
static void DisplayPER(struct _PER* pPER);
static void DisplayPED(struct _PED* pPED);
static void DisplayCCC(struct _CCC* pCCC);
static void DisplayPDA(const wchar_t* pIndent, struct _PDA* pPDA);
static void DisplayPDL(const wchar_t* pIndent, struct _PDL* pPDL);
static void DisplayPD3(const wchar_t* pIndent, struct _PD3* pPDL);
static void ClearOutputRow(struct _SurveyRow* outputRow);

EMObsReader::EMObsReader(const std::string& _filespec) : filespec(_filespec) {
    this->reader = new EMObsReaderBase(filespec);
}

int EMObsReader::Process(std::list<struct _SurveyRow*>& outputRowsAdd) {
    int ret = 0;
    struct _EBS* pEBS = nullptr;
    std::list<struct _IDA*> IDAList;
    std::list<struct _CMS*> CMSList;    // List of 2 items max
    struct _PER* pPER = nullptr;
    std::list<struct _CCC*> CCCList;    // List of 2 items max
    bool finished = false;

    ret = reader->ReadFile();

    if (ret == 0) {


        unsigned char* p;
        int size;
        char TLC[4];
        ret = reader->GetFirstTLC((void**)&p, &size, TLC);

        while (ret == 0 && finished == false) {
            unsigned char* pAfterTLC = (unsigned char*)(p + 3);
            int fixedSize = size;


            if (strcmp(TLC, "EBS") == 0) {

                // Check this is only one EBS
                if (pEBS != nullptr)
                    wprintf(L"*** Warning more then one EBS detected!\n");

                pEBS = GetEBS();
                if (pEBS == nullptr) {
                    wprintf(L"*** Error EBS not found!\n");
                    break;
                }
                reader->SetSeekPointerToReadPointer();

                // Print known information
                DisplayEBS(pEBS);
            }
			// Get the IDA structures which contain the Measurement, 2D and 3D point data
            else if (strcmp(TLC, "IDA") == 0) {

                struct _IDA* pIDA = GetIDA();
                reader->SetSeekPointerToReadPointer();
                IDAList.push_back(pIDA);

                // Print known information
                DisplayIDA(pIDA);
            }
			// Get the CMS structures which contain the media file information (list of files) for the left and right cameras
            else if (strcmp(TLC, "CMS") == 0) {

                // Check this is a max of 2 CMS
                size_t count = CMSList.size();
                if (count > 2)
                    wprintf(L"*** Warning more then two CMS (Media Info Lists) detected! (%zu found)\n", count);

                struct _CMS* pCMS = GetCMS();
                reader->SetSeekPointerToReadPointer();
                CMSList.push_back(pCMS);

                // Print known information
                DisplayCMS(pCMS);
            }
			// Get the PER structure which contains the period information
            else if (strcmp(TLC, "PER") == 0) {
                
                // Check this is only one EBS
                if (pPER != nullptr)
                    wprintf(L"*** Warning more then one PER (Period List) detected!\n");

                pPER = GetPER();
                reader->SetSeekPointerToReadPointer();

                // Print known information
                DisplayPER(pPER);
            }
			// Get the CCC structures which contain the camera calibration information for the left and right cameras
            else if (strcmp(TLC, "CCC") == 0) {

                // Check this is a max of 2 CMS
                size_t count = CCCList.size();
                if (count > 2)
                    wprintf(L"*** Warning more then two CCC (Calibration Data) detected! (%zu found)\n", count);

                struct _CCC* pCCC = GetCCC();
                reader->SetSeekPointerToReadPointer();
                CCCList.push_back(pCCC);

                // Print known information
                DisplayCCC(pCCC);
            }
            else {
                printf("%08lX %s:\t%05i\t%i\t%i\n", reader->GetReadPointer(), TLC, size, (int)*pAfterTLC, fixedSize);
                // Display raw data
                hexDump("*** Unsupported", -1, p, (int)size);
                finished = true;
            }

            ret = reader->GetNextTLC((void**)&p, &size, TLC);
        }

        if (ret == 0 && pEBS != nullptr) {

            // Populate the OutputRow list

            // Grab the row from the previous _OutputRow item or if the list is empty set it to 1
            int row = 1;
            if (!outputRowsAdd.empty())
                row = outputRowsAdd.back()->row + 1;


            // pEBS->wsPictureDirectory;    // Path
            struct _CIN* pCIN = pEBS->pCIN; // OpCode data

            // Convert the filespec to a std::filesystem::path object
            fs::path fullPath(filespec);

            // Extract the path (without the filename)
            fs::path PathEMObs = fullPath.parent_path();

            // Extract the filename with extension
            fs::path FileEMObs = fullPath.filename();


            // Range-based for loop (modern C++11)
            for (_IDA* itemIDA : IDAList) {

                struct _SurveyRow* outputRow;
                struct _FRA* pFRA = itemIDA->pFRA;

                // Collect the PDA 2D point data
                for (_PDA* itemPDA : itemIDA->TypePDA.PDAList) {

                    outputRow = new struct _SurveyRow;
                    ClearOutputRow(outputRow);
                    outputRow->row = row++;

                    outputRow->PathEMObs = PathEMObs;
                    outputRow->FileEMObs = FileEMObs;
                    outputRow->Period = itemIDA->wsPeriodName;
                    outputRow->opCode = pCIN->matValue[0][0];
                    outputRow->Path = pEBS->wsPictureDirectory;
                    if (pFRA->iCameraZeroLeftOneRight == 0) {// Left Camera
                        outputRow->rowType = Point2DLeftCamera;
                        outputRow->FileL = pFRA->wsMediaFile;
                        outputRow->FrameL = pFRA->iFrameIndex;
                        outputRow->PointLX1 = itemPDA->pCPT->X;
                        outputRow->PointLY1 = itemPDA->pCPT->Y;
                    }
                    else if (pFRA->iCameraZeroLeftOneRight == 1) {// Right Camera
                        outputRow->rowType = Point2DRightCamera;
                        outputRow->FileR = pFRA->wsMediaFile;
                        outputRow->FrameR = pFRA->iFrameIndex;
                        outputRow->PointRX1 = itemPDA->pCPT->X;
                        outputRow->PointRY1 = itemPDA->pCPT->Y;
                    }
                    else
                        assert(false);

                    outputRow->Family = itemPDA->matCollectionValues[0][0];
                    outputRow->Genus = itemPDA->matCollectionValues[1][0];
                    outputRow->Species = itemPDA->matCollectionValues[2][0];
                    if (itemPDA->matCollectionValues[4][0].empty())
                        outputRow->count = 1;
                    else {
                        try {
                            outputRow->count = std::stoi(itemPDA->matCollectionValues[4][0]);
                        }
                        catch (const std::exception& e) {
                            printf("Process: Bad fish count in PDA, on row: %i, setting count to -1, %s.", outputRow->row, e.what());
                            outputRow->count = -1;
                        }
                    }

                    outputRowsAdd.push_back(outputRow);
                }
                // Collect the PDL 3D measurement point data
                for (_PDL* itemPDL : itemIDA->TypePDL.PDLList) {

                    // It is assumes that the base FRA is the left camera and the PDL>FRA is the right camera
                    assert(pFRA->iCameraZeroLeftOneRight == 0);
                    assert(itemPDL->pFRA->iCameraZeroLeftOneRight == 1);

                    outputRow = new struct _SurveyRow;
                    ClearOutputRow(outputRow);
                    outputRow->row = row++;

                    outputRow->PathEMObs = PathEMObs;
                    outputRow->FileEMObs = FileEMObs;
                    outputRow->Period = itemIDA->wsPeriodName;
                    outputRow->opCode = pCIN->matValue[0][0];
                    outputRow->rowType = MeasurementPoint3D;
                    outputRow->Path = pEBS->wsPictureDirectory;
                    outputRow->FileL = pFRA->wsMediaFile;
                    outputRow->FrameL = pFRA->iFrameIndex;
                    outputRow->PointLX1 = itemPDL->pCPT1->X;
                    outputRow->PointLY1 = itemPDL->pCPT1->Y;
                    outputRow->PointLX2 = itemPDL->pCPT2->X;
                    outputRow->PointLY2 = itemPDL->pCPT2->Y;
                    outputRow->FileR = itemPDL->pFRA->wsMediaFile;
                    outputRow->FrameR = itemPDL->pFRA->iFrameIndex;
                    outputRow->PointRX1 = itemPDL->pCPT3->X;
                    outputRow->PointRY1 = itemPDL->pCPT3->Y;
                    outputRow->PointRX2 = itemPDL->pCPT4->X;
                    outputRow->PointRY2 = itemPDL->pCPT4->Y;

                    outputRow->Family = itemPDL->matCollectionValues[0][0];
                    outputRow->Genus = itemPDL->matCollectionValues[1][0];
                    outputRow->Species = itemPDL->matCollectionValues[2][0];
                    if (itemPDL->matCollectionValues[4][0].empty())
                        outputRow->count = 1;
                    else {
                        try {
                            outputRow->count = std::stoi(itemPDL->matCollectionValues[4][0]);
                        }
                        catch (const std::exception& e) {
                            printf("Process: Bad fish count in PDL, on row: %i, setting count to -, %s.", outputRow->row, e.what());
                            outputRow->count = -1;
                        }
                    }

                    outputRowsAdd.push_back(outputRow);
                }
                // Collect the PD3 3D point data
                for (_PD3* itemPD3 : itemIDA->TypePD3.PD3List) {


                    outputRow = new struct _SurveyRow;
                    ClearOutputRow(outputRow);
                    outputRow->row = row++;

                    outputRow->PathEMObs = PathEMObs;
                    outputRow->FileEMObs = FileEMObs;
                    outputRow->Period = itemIDA->wsPeriodName;
                    outputRow->opCode = pCIN->matValue[0][0];
                    outputRow->Path = pEBS->wsPictureDirectory;
                    if (pFRA->iCameraZeroLeftOneRight == 0 && itemPD3->pFRA->iCameraZeroLeftOneRight == 1) {// should always be the case
                        outputRow->rowType = Point3D;
                        outputRow->FileL = pFRA->wsMediaFile;
                        outputRow->FrameL = pFRA->iFrameIndex;
                        outputRow->PointLX1 = itemPD3->pCPT1->X;
                        outputRow->PointLY1 = itemPD3->pCPT1->Y;                        

                        outputRow->FileR = itemPD3->pFRA->wsMediaFile;
                        outputRow->FrameR = itemPD3->pFRA->iFrameIndex;
                        outputRow->PointRX1 = itemPD3->pCPT2->X;
                        outputRow->PointRY1 = itemPD3->pCPT2->Y;
                    }
                    else
                        assert(false);

                    outputRow->Family = itemPD3->matCollectionValues[0][0];
                    outputRow->Genus = itemPD3->matCollectionValues[1][0];
                    outputRow->Species = itemPD3->matCollectionValues[2][0];
                    if (itemPD3->matCollectionValues[4][0].empty())
                        outputRow->count = 1;
                    else {
                        try {
                            outputRow->count = std::stoi(itemPD3->matCollectionValues[4][0]);
                        }
                        catch (const std::exception& e) {
                            printf("Process: Bad fish count in PDS, on row: %i, setting count to -1, %s.", outputRow->row, e.what());
                            outputRow->count = -1;
                        }
                    }

                    outputRowsAdd.push_back(outputRow);
                }
            }
        }
    }


    // Clear
    if (pEBS)
        delete pEBS;

    return ret;
}

static void DisplayEBS(struct _EBS* pEBS) {
    wprintf(L"%08lX EBS: Picture Directory=[%ls]\n", pEBS->fileSeekPointer, pEBS->wsPictureDirectory.c_str());

    printf("%08lX EBS>CIN:  (Information Fields)\n", pEBS->pCIN->fileSeekPointer);
    if (pEBS->pCIN != nullptr) {
        for (int i = 0; i < pEBS->pCIN->matTitle.size(); i++) {
            if (!pEBS->pCIN->matTitle[i][0].empty() || !pEBS->pCIN->matValue[i][0].empty()) {
                wprintf(L"       %02i: %ls = [%ls]\n",
                    i,
                    pEBS->pCIN->matTitle[i][0].c_str(),
                    pEBS->pCIN->matValue[i][0].c_str());
            }
        }
    }
    else
        wprintf(L"       error pEBS->pCIN null ptr\n");

    printf("%08lX EBS>PTN:  (Collection Fields Titles)\n", pEBS->pPTN->fileSeekPointer);
    if (pEBS->pPTN != nullptr) {
        for (int i = 0; i < pEBS->pPTN->matCollectionHeadings.size(); i++) {
            if (!pEBS->pPTN->matCollectionHeadings[i][0].empty()) {
                wprintf(L"       %02i: Title = [%ls]\n",
                    i,
                    pEBS->pPTN->matCollectionHeadings[i][0].c_str());
            }
        }
    }
    else
        wprintf(L"       error null ptr\n");

    wprintf(L"\n");
}


static void DisplayIDA(struct _IDA* pIDA) {

    if (pIDA != nullptr && pIDA->pFRA != nullptr) {

        wprintf(L"%08lX IDA>FRA: Left Frame=%i (Camera=%s) Media=%ls\n",
            pIDA->fileSeekPointer,
            pIDA->pFRA->iFrameIndex,
            pIDA->pFRA->iCameraZeroLeftOneRight == 0 ? L"Left" : L"Right",
            pIDA->pFRA->wsMediaFile.c_str());

        //???
        if (pIDA->pFRA->iFrameIndex == 2243)
            pIDA->pFRA->iFrameIndex = 2243;   // In File 6 at 2243 example of 3 PDA and two PDL 

        if (!(pIDA->pFRA->iCameraZeroLeftOneRight == 0 || pIDA->pFRA->iCameraZeroLeftOneRight == 1))
            wprintf(L"         ***IDA>FRA>iCameraZeroLeftOneRight should be either 0 or 1, and it is %i***\n", pIDA->pFRA->iCameraZeroLeftOneRight);


        if (pIDA->TypePDA.iPDACount > 0) {
            wprintf(L"      PDA Count=%i  2D Points\n",
                pIDA->TypePDA.iPDACount);

            for (auto pPDA : pIDA->TypePDA.PDAList) {
                DisplayPDA(L"    ", pPDA);
            }
        }

        //hexDump("  IDA>Data1", -1, pIDA->data1.bData, sizeof(pIDA->data1.bData)/*16*/);
        wprintf(L"    IDA>Data1: As Int32:%d,%d,%d,%d  Double:=%.6f,=%.6f\n", 
                                pIDA->data1.ints[0], pIDA->data1.ints[1], pIDA->data1.ints[2], pIDA->data1.ints[3], 
            pIDA->data1.doubles[0], pIDA->data1.doubles[1]);

        wprintf(L"    IDA>Period:[%ls]\n", pIDA->wsPeriodName.c_str());


        if (pIDA->TypePDL.iPDLCount > 0) {
            wprintf(L"      PDL Count=%i  Measurements\n",
                pIDA->TypePDL.iPDLCount);

            for (auto pPDL : pIDA->TypePDL.PDLList) {
                DisplayPDL(L"    ", pPDL);
            }
        }

        if (pIDA->TypePD3.iPD3Count > 0) {
            wprintf(L"      PD3 Count=%i  3D Points\n",
                pIDA->TypePD3.iPD3Count);

            for (auto pPD3 : pIDA->TypePD3.PD3List) {
                DisplayPD3(L"    ", pPD3);
            }
        }

        //hexDump("  IDA>Data2", -1, pIDA->data2.bData, sizeof(pIDA->data2.bData));
        wprintf(L"    IDA>Data2: As Int32:%d,%d,%d,%d  Double:=%.6f,=%.6f\n",
            pIDA->data2.ints[0], pIDA->data2.ints[1], pIDA->data2.ints[2], pIDA->data2.ints[3],
            pIDA->data2.doubles[0], pIDA->data2.doubles[1]);
    }
    else {
        if (pIDA == nullptr)
            wprintf(L"       error pIDA null ptr\n");
        else if (pIDA->pFRA == nullptr)
            wprintf(L"       error pIDA->pFRA null ptr\n");
    }

    wprintf(L"\n");
}

// Display a PDA which is believed to be a EventMeasure Point
static void DisplayPDA(const wchar_t* pIndent, struct _PDA* pPDA) {
    if (pPDA->pCPT != nullptr) {
        wprintf(L"%sPDA>CPT: X:%.2f Y:%.2f\n",
            pIndent, pPDA->pCPT->X, pPDA->pCPT->Y);
    }
    else
        wprintf(L"%s   error pPDA->pCPT null ptr\n", pIndent);

    // Display MAT array
    for (int i = 0; i < pPDA->matCollectionValues.size(); i++) {
        if (!pPDA->matCollectionValues[i][0].empty()) {
            wprintf(L"%s   %02i: Values = [%ls]\n",
                pIndent,
                i,
                pPDA->matCollectionValues[i][0].c_str());
        }
    }
}

// Display a PDL which is believed to be a EventMeasure set of measurement point
static void DisplayPDL(const wchar_t* pIndent, struct _PDL* pPDL) {

    wprintf(L"    Left CPT Count: %i (should always be 2)\n", pPDL->iData1);
    if (pPDL->pCPT1 != nullptr) {
        wprintf(L"    PDL>CPT1: X:%.2f, Y:%.2f\n",
            pPDL->pCPT1->X, pPDL->pCPT1->Y);

    }
    else
        wprintf(L"       error pPDL->pCPT1 null ptr\n");

    if (pPDL->pCPT2 != nullptr) {
        wprintf(L"    PDL>CPT2: X:%.2f, Y:%.2f\n",
            pPDL->pCPT2->X, pPDL->pCPT2->Y);
    }
    else
        wprintf(L"       error pPDL->pCPT2 null ptr\n");

    wprintf(L"    Right CPT Count: %i (should always be 2)\n", pPDL->iData2);

    if (pPDL->pCPT3 != nullptr) {
        wprintf(L"    PDL>CPT3: X:%.2f, Y:%.2f\n",
            pPDL->pCPT3->X, pPDL->pCPT3->Y);

    }
    else
        wprintf(L"       error pPDL->pCPT3 null ptr\n");

    if (pPDL->pCPT4 != nullptr) {
        wprintf(L"    PDL>CPT4: X:%.2f, Y:%.2f\n",
            pPDL->pCPT4->X, pPDL->pCPT4->Y);

    }
    else
        wprintf(L"       error pPDL->pCPT4 null ptr\n");


    for (int i = 0; i < pPDL->matCollectionValues.size(); i++) {
        if (!pPDL->matCollectionValues[i][0].empty()) {
            wprintf(L"       %02i: Values = [%ls]\n",
                i,
                pPDL->matCollectionValues[i][0].c_str());
        }
    }

    wprintf(L"       PDL>FRA: Right Frame=%i (Camera=%s) Media=%ls\n",
        pPDL->pFRA->iFrameIndex,
        pPDL->pFRA->iCameraZeroLeftOneRight == 0 ? L"Left" : L"Right",
        pPDL->pFRA->wsMediaFile.c_str());

}


// Display a PD3 which is believed to be a EventMeasure set of 3D point
static void DisplayPD3(const wchar_t* pIndent, struct _PD3* pPD3) {

    //wprintf(L"    Left CPT Count: %i (should always be 2)\n", pPD3->iData1);
    if (pPD3->pCPT1 != nullptr) {
        wprintf(L"    PDL>CPT1: X:%.2f, Y:%.2f\n",
            pPD3->pCPT1->X, pPD3->pCPT1->Y);

    }
    else
        wprintf(L"       error pPD3->pCPT1 null ptr\n");

    if (pPD3->pCPT2 != nullptr) {
        wprintf(L"    PD3>CPT2: X:%.2f, Y:%.2f\n",
            pPD3->pCPT2->X, pPD3->pCPT2->Y);
    }
    else
        wprintf(L"       error pPD3->pCPT2 null ptr\n");

    for (int i = 0; i < pPD3->matCollectionValues.size(); i++) {
        if (!pPD3->matCollectionValues[i][0].empty()) {
            wprintf(L"       %02i: Values = [%ls]\n",
                i,
                pPD3->matCollectionValues[i][0].c_str());
        }
    }

    wprintf(L"       PD3>FRA: Right Frame=%i (Camera=%s) Media=%ls\n",
        pPD3->pFRA->iFrameIndex,
        pPD3->pFRA->iCameraZeroLeftOneRight == 0 ? L"Left" : L"Right",
        pPD3->pFRA->wsMediaFile.c_str());

}


/// <summary>
/// Display a media list for the left or right camera, and 
/// the frame count and frame rate for each media file in that list
/// </summary>
/// <param name="pCMS">List of Media Info structures</param>
static void DisplayCMS(struct _CMS* pCMS) {

    if (pCMS != nullptr) {

        wprintf(L"%08lX CMS: Media Count=%i\n",
            pCMS->fileSeekPointer,
            pCMS->Count);

        for (auto pMSI : pCMS->pMSIArray) {
            DisplayMSI(pMSI);
        }
    }
}

/// <summary>
/// Display a single media info structure which is a media file for either the left or right 
/// camera, and the frame count and frame rate for that media file
/// <summary>
/// /// <param name="pMSI">Single Media info structure</param>
static void DisplayMSI(struct _MSI* pMSI) {
    
    if (pMSI != nullptr) {
        wprintf(L"%08lX CMS>MSI: Media=%ls FrameCount=%lli FrameRate=%.2f\n",
            pMSI->fileSeekPointer,
            pMSI->wsMediaFile.c_str(),
            pMSI->FrameCount,
            pMSI->FrameRate);
	}
}

static void DisplayPER(struct _PER* pPER) {

    if (pPER != nullptr) {

        wprintf(L"%08lX PER: Period Count=%i\n",
            pPER->fileSeekPointer,
            pPER->PeriodCount);

        for (auto pPED : pPER->pPEDArray) {
            DisplayPED(pPED);
        }
    }
}


/// <summary>
/// Display a single period structure 
/// <summary>
/// /// <param name="pPED">Single Period structure</param>
static void DisplayPED(struct _PED* pPED) {

    if (pPED != nullptr) {
        wprintf(L"%08lX PER>PED: Period Name:%s %s %s Start:%i, End:%i\n",
            pPED->fileSeekPointer,
			pPED->pFRAStart->iCameraZeroLeftOneRight == 0 ? L"Left" : L"Right",
			pPED->pFRAStart->wsMediaFile.c_str(),
            pPED->wsPeriodName.c_str(),
            pPED->pFRAStart->iFrameIndex,
            pPED->pFRAEnd->iFrameIndex);
    }
}

static void DisplayCCC(struct _CCC* pCCC) {
    
    if (pCCC != nullptr) {
        wprintf(L"%08lX CCC>CAM  CAMERA CALIBRATION\n",
            pCCC->fileSeekPointer);

        hexDump("  CCC>Data1", -1, pCCC->bData1, sizeof(pCCC->bData1)/*5*/);

        wprintf(L"    %s\n", pCCC->pCAM->wsCameraName.c_str());
        wprintf(L"    %s\n", pCCC->pCAM->wsDerivedFrom.c_str());

        wprintf(L"    X Pixel Size: %.4f mm\n", pCCC->pCAM->xPixelSize);
        wprintf(L"    Y Pixel Size: %.4f mm\n", pCCC->pCAM->yPixelSize);
        wprintf(L"    Frame Height: %i pixels\n", pCCC->pCAM->frameHeight);
        wprintf(L"    Frame Width: %i pixels\n", pCCC->pCAM->frameWidth);
        wprintf(L"    X Principal Point Offset: %.4f mm\n", pCCC->pCAM->xPPOffset);
        wprintf(L"    Y Principal Point Offset: %.4f mm\n", pCCC->pCAM->yPPOffset);
        wprintf(L"    Focal Length: %.4f mm\n", pCCC->pCAM->focalLength);
        wprintf(L"    K3 Radial Distortion: %.6f mm^-2\n", pCCC->pCAM->k3RadialDistortion);
        wprintf(L"    K5 Radial Distortion: %.6f mm^-4\n", pCCC->pCAM->k5RadialDistortion);
        wprintf(L"    K7 Radial Distortion: %.6f mm^-6\n", pCCC->pCAM->k7RadialDistortion);
        wprintf(L"    P1 Decentering/Tangential Distortion: %.6f mm^-1\n", pCCC->pCAM->p1DecenteringDistortion);
        wprintf(L"    P2 Decentering/Tangential Distortion: %.6f mm^-1\n", pCCC->pCAM->p2DecenteringDistortion);
        wprintf(L"    Orthogonality: %.6f\n", pCCC->pCAM->orthogonality);
        wprintf(L"    Affinity: %.6f\n", pCCC->pCAM->affinity);
        wprintf(L"    Camera X: %.4f mm\n", pCCC->pCAM->cameraX);
        wprintf(L"    Camera Y: %.4f mm\n", pCCC->pCAM->cameraY);
        wprintf(L"    Camera Z: %.4f mm\n", pCCC->pCAM->cameraZ);
        wprintf(L"    Omega: %.2f degrees\n", pCCC->pCAM->omega);
        wprintf(L"    Phi: %.2f degrees\n", pCCC->pCAM->phi);
        wprintf(L"    Kappa: %.2f degrees\n", pCCC->pCAM->kappa);

        hexDump("  CCC>Data2", -1, pCCC->bData2, sizeof(pCCC->bData2)/*8*/);
        wprintf(L" Frame Height: %i pixels\n", pCCC->frameHeight);
        wprintf(L" Frame Width: %i pixels\n", pCCC->frameWidth);
    }
}



static void hexDump(const char* desc, long seekOffset, void* data, int len) {

    int i;
    unsigned char buff[17]{};
    unsigned char* pc = (unsigned char*)data;

    // Output description if given.
    if (desc != NULL)
        printf_s("%s:\n", desc);


    if (len == 0) {
        printf_s("  ZERO LENGTH\n");
        return;
    }
    if (len < 0) {
        printf_s("  NEGATIVE LENGTH: %i\n", len);
        return;
    }


    // Process every byte in the data.
    for (i = 0; i < len; i++) {
        // Multiple of 16 means new line (with line offset).
        if ((i % 16) == 0) {
            // Just don't print ASCII for the zeroth line.
            if (i != 0) {
                printf_s("  %s\n", buff);
            }

            // Output the offset.
            printf_s("  %04x ", i);
        }

        // Now the hex code for the specific character.
        printf_s(" %02x", pc[i]);

        // And store a printable ASCII character for later.
        if ((pc[i] < 0x20) || (pc[i] > 0x7e))
            buff[i % 16] = '.';
        else
            buff[i % 16] = pc[i];
        buff[(i % 16) + 1] = '\0';
    }

    // Pad out last line if not exactly 16 characters.
    while ((i % 16) != 0) {
        printf_s("   ");
        i++;
    }

    // And print the final ASCII bit.
    printf_s("  %s\n", buff);
}


/// <summary>
/// File Header
/// TLC=EBS Version = 4 or 5
/// Children:
///     WString:wsPictureDirectory Directory where the media is located
///     TLC:CIN
///     TLC:PTN
/// </summary>

struct _EBS* EMObsReader::GetEBS() {

    int ret = 0;
    struct _EBS* pEBS = new _EBS();

    if (pEBS != nullptr) {

        pEBS->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pEBS->cTLC, 3);
        reader->GetNextAsFixedChar(&pEBS->cTLCVersion, 1);
        if (memcmp(pEBS->cTLC, "EBS", 3) == 0) {

            if (pEBS->cTLCVersion == 4 || pEBS->cTLCVersion == 5) {
                pEBS->wsPictureDirectory = reader->GetNextAsWString();

                pEBS->pCIN = GetCIN();
                pEBS->pPTN = GetPTN();
            }
            else {
                printf("***GetEBS Error EBS, unexpected TLC version of %i found\n", (int)pEBS->cTLCVersion);
                delete pEBS;
                pEBS = nullptr;
            }
        }
        else {
            printf("***GetEBS Error EBS expected not found\n");
            delete pEBS;
            pEBS = nullptr;
        }
    }

    return pEBS;
}

/// <summary>
/// Information Fields
/// TLC=CIN Version = 0
/// Children:
///     TLC:MAT Information Field Titles
///     TLC:MAT Information Field Values
/// </summary>
struct _CIN* EMObsReader::GetCIN() {

    int ret = 0;
    struct _CIN* pCIN = new _CIN();

    if (pCIN != nullptr) {

        pCIN->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pCIN->cTLC, 3);
        reader->GetNextAsFixedChar(&pCIN->cTLCVersion, 1);
        if (memcmp(pCIN->cTLC, "CIN", 3) == 0) {

            if (pCIN->cTLCVersion == 0) {
                pCIN->matTitle = reader->GetNextAsMATwstring();
                pCIN->matValue = reader->GetNextAsMATwstring();
            }
            else {
                printf("***GetCIN Error CIN, unexpected TLC version of %i found\n", (int)pCIN->cTLCVersion);
                delete pCIN;
                pCIN = nullptr;
            }
        }
        else {
            printf("***GetCIN Error CIN expected not found\n");
            delete pCIN;
            pCIN = nullptr;
        }
    }

    return pCIN;
}


/// <summary>
/// Configurable header titles for what we are collecting
/// TLC=PTN Version = 0
/// Children:
///     TLC:MAT Collection Field Titles
/// </summary>
struct _PTN* EMObsReader::GetPTN() {

    int ret = 0;
    struct _PTN* pPTN = new _PTN();

    if (pPTN != nullptr) {

        pPTN->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPTN->cTLC, 3);
        reader->GetNextAsFixedChar(&pPTN->cTLCVersion, 1);
        if (memcmp(pPTN->cTLC, "PTN", 3) == 0) {
            if (pPTN->cTLCVersion == 0) {
                pPTN->matCollectionHeadings = reader->GetNextAsMATwstring();
                pPTN->iData1 = reader->GetNextAsInt32();
            }
            else {
                printf("***GetPTN Error PTN, unexpected TLC version of %i found\n", (int)pPTN->cTLCVersion);
                delete pPTN;
                pPTN = nullptr;
            }
        }
        else {
            printf("***GetPTN Error PTN expected not found\n");
            delete pPTN;
            pPTN = nullptr;
        }
    }

    return pPTN;
}


/// <summary>
/// Measurement data Top level element
/// TLC=IDA Version = 5
/// Children:
///     TLC:FRA 
///     TLC:PDA
///     TLC:PDL
///     TLC:PD3
///     TLC:MAT
///  TODO
/// </summary>
struct _IDA* EMObsReader::GetIDA() {

    int ret = 0;
    bool failed = false;
    struct _IDA* pIDA = new _IDA();

    if (pIDA != nullptr) {

        pIDA->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pIDA->cTLC, 3);
        reader->GetNextAsFixedChar(&pIDA->cTLCVersion, 1);
        if (memcmp(pIDA->cTLC, "IDA", 3) == 0) {
            if (pIDA->cTLCVersion == 5) {

                pIDA->pFRA = GetFRA();

                // Get PDA Count
                pIDA->TypePDA.iPDACount = reader->GetNextAsInt32();        // Seen as 3, 2, 1 & 0 maybe 3, 2 & 1 are PDA and 0 is PDL

                int i;

                for (i = 0; i < pIDA->TypePDA.iPDACount; i++) {
                    struct _PDA* pPDA = GetPDA();
                    if (pPDA != nullptr) {
                        pIDA->TypePDA.PDAList.push_back(pPDA);
                    }
                    else {
                        failed = true;
                        break;
                    }
                }

                if (!failed) {
                    // Get data and period

                    // Get the data
                    reader->GetNextAsFixedChar(pIDA->data1.bData, sizeof(pIDA->data1.bData));
                    // Get the Period Name
                    pIDA->wsPeriodName = reader->GetNextAsWString();


                    // Get PDL Count
                    pIDA->TypePDL.iPDLCount = reader->GetNextAsInt32();        // Seen as 3, 2, 1 & 0 maybe 3, 2 & 1 are PDA and 0 is PDL

                    for (i = 0; i < pIDA->TypePDL.iPDLCount; i++) {
                        struct _PDL* pPDL = GetPDL();
                        if (pPDL != nullptr) {
                            pIDA->TypePDL.PDLList.push_back(pPDL);
                        }
                        else {
                            failed = true;
                            break;
                        }
                    }

                    if (!failed) {

                        // Get PD3 Count
                        pIDA->TypePD3.iPD3Count = reader->GetNextAsInt32();        // Seen as 3, 2, 1 & 0 maybe 3, 2 & 1 are PDA and 0 is PDL

                        for (i = 0; i < pIDA->TypePD3.iPD3Count; i++) {
                            struct _PD3* pPD3 = GetPD3();
                            if (pPD3 != nullptr) {
                                pIDA->TypePD3.PD3List.push_back(pPD3);
                            }
                            else {
                                failed = true;
                                break;
                            }
                        }

                        if (!failed) {
                            // Get end type data
                            reader->GetNextAsFixedChar(pIDA->data2.bData, sizeof(pIDA->data2.bData));
                        }
                    }
                }
            }
            else {
                printf("***GetIDA Error IDA, unexpected TLC version of %i found\n", (int)pIDA->cTLCVersion);
                delete pIDA;
                pIDA = nullptr;
            }
        }
        else {
            printf("***GetIDA Error IDA expected not found\n");
            delete pIDA;
            pIDA = nullptr;
        }
    }

    return pIDA;
}


/// <summary>
/// Frame data: Which left or right camera, frame number and media file
/// TLC=FRA Version = 1
/// Children:
///     TLC:MAT
/// </summary>
struct _FRA* EMObsReader::GetFRA() {

    int ret = 0;
    struct _FRA* pFRA = new _FRA();

    if (pFRA != nullptr) {

        pFRA->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pFRA->cTLC, 3);
        reader->GetNextAsFixedChar(&pFRA->cTLCVersion, 1);
        if (memcmp(pFRA->cTLC, "FRA", 3) == 0) {
            if (pFRA->cTLCVersion == 1) {
                pFRA->iCameraZeroLeftOneRight = reader->GetNextAsInt32();
                pFRA->iFrameIndex = reader->GetNextAsInt32();	// Frame Number
                pFRA->wsMediaFile = reader->GetNextAsWString();
            }
            else {
                printf("***GetFRA Error FRA, unexpected TLC version of %i found\n", (int)pFRA->cTLCVersion);
                delete pFRA;
                pFRA = nullptr;
            }
        }
        else {
            printf("***GetFRA Error FRA expected not found\n");
            delete pFRA;
            pFRA = nullptr;
        }
    }

    return pFRA;
}


/// <summary>
/// 2D Point data
/// TLC=PDA Version = 0 or 1
/// Children:
///     TLC:CPT
///     TLC:MAT Collection Values
///  TODO
/// </summary>
struct _PDA* EMObsReader::GetPDA() {

    int ret = 0;
    struct _PDA* pPDA = new _PDA();

    if (pPDA != nullptr) {

        pPDA->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPDA->cTLC, 3);
        reader->GetNextAsFixedChar(&pPDA->cTLCVersion, 1);
        // No sure why both 0 and 1 is possible the value most normally is 1
        if (memcmp(pPDA->cTLC, "PDA", 3) == 0) {
            if (pPDA->cTLCVersion == 0 || pPDA->cTLCVersion == 1) {

                pPDA->pCPT = GetCPT();
                pPDA->matCollectionValues = reader->GetNextAsMATwstring();

                if (pPDA->cTLCVersion == 1) {
                    reader->GetNextAsFixedChar(pPDA->bData, sizeof(pPDA->bData));
                }
            }
            else {
                printf("***GetPDA Error PDA, unexpected TLC version of %i found\n", (int)pPDA->cTLCVersion);
                delete pPDA;
                pPDA = nullptr;
            }
        }
        else {
            printf("***GetPDA Error PDA expected not found\n");
            delete pPDA;
            pPDA = nullptr;
        }
    }

    return pPDA;
}


/// <summary>
/// 3D Measurement Point (2x3D points in the left camera frame and 2x3D points in the right camera frame)
/// TLC=PDL Version = 1
/// Children:
///     TLC: CPT
///     TLC: FRA
///     TLC: MAT 
/// </summary>
struct _PDL* EMObsReader::GetPDL() {

    int ret = 0;
    struct _PDL* pPDL = new _PDL();

    if (pPDL != nullptr) {

        pPDL->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPDL->cTLC, 3);
        reader->GetNextAsFixedChar(&pPDL->cTLCVersion, 1);
        if (memcmp(pPDL->cTLC, "PDL", 3) == 0) {
            if (pPDL->cTLCVersion == 1) {

                pPDL->iData1 = reader->GetNextAsInt32();        // Seen as 2
                if (pPDL->iData1 != 2)
                    wprintf(L"*** Warning PDL iData1 not 2\n");

                pPDL->pCPT1 = GetCPT();
                pPDL->pCPT2 = GetCPT();
                pPDL->iData2 = reader->GetNextAsInt32();        // Seen as 2
                if (pPDL->iData2 != 2)
                    wprintf(L"*** Warning PDL iData2 not 2\n");

                pPDL->pCPT3 = GetCPT();
                pPDL->pCPT4 = GetCPT();
                pPDL->pFRA = GetFRA();
                pPDL->matCollectionValues = reader->GetNextAsMATwstring();
            }
            else {
                printf("***GetPDL Error PDL, unexpected TLC version of %i found\n", (int)pPDL->cTLCVersion);
                delete pPDL;
                pPDL = nullptr;
            }
        }
        else {
            printf("***GetPDL Error PDL expected not found\n");
            delete pPDL;
            pPDL = nullptr;
        }
    }

    return pPDL;
}


/// <summary>
/// 3D Point (2x3D points on either the left or right camera)
/// TLC=PDL Version = 0
/// Children:
///     TLC: CPT
///     TLC: FRA
///     TLC: MAT 
/// </summary>
struct _PD3* EMObsReader::GetPD3() {

    int ret = 0;
    struct _PD3* pPD3 = new _PD3();

    if (pPD3 != nullptr) {

        pPD3->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPD3->cTLC, 3);
        reader->GetNextAsFixedChar(&pPD3->cTLCVersion, 1);
        if (memcmp(pPD3->cTLC, "PD3", 3) == 0) {
            if (pPD3->cTLCVersion == 0) {

                pPD3->pCPT1 = GetCPT();
                pPD3->pCPT2 = GetCPT();
                pPD3->pFRA = GetFRA();
                pPD3->matCollectionValues = reader->GetNextAsMATwstring();
            }
            else {
                printf("***GetPD3 Error PD3, unexpected TLC version of %i found\n", (int)pPD3->cTLCVersion);
                delete pPD3;
                pPD3 = nullptr;
            }
        }
        else {
            printf("***GetPD3 Error PD3 expected not found\n");
            delete pPD3;
            pPD3 = nullptr;
        }
    }

    return pPD3;
}

/// <summary>
/// Coordinate Point
/// TLC=CPT Version = 0
/// </summary>
struct _CPT* EMObsReader::GetCPT() {

    int ret = 0;
    struct _CPT* pCPT = new _CPT();

    if (pCPT != nullptr) {

        reader->GetNextAsFixedChar(pCPT->cTLC, 3);
        reader->GetNextAsFixedChar(&pCPT->cTLCVersion, 1);
        if (memcmp(pCPT->cTLC, "CPT", 3) == 0) {
            if (pCPT->cTLCVersion == 0) {

                pCPT->X = reader->GetNextAsDouble();
                pCPT->Y = reader->GetNextAsDouble();
            }
            else {
                printf("***GetCPT Error CPT, unexpected TLC version of %i found\n", (int)pCPT->cTLCVersion);
                delete pCPT;
                pCPT = nullptr;
            }
        }
        else {
            printf("***GetCPT20 Error CPT expected not found\n");
            delete pCPT;
            pCPT = nullptr;
        }
    }

    return pCPT;
}


/// <summary>
/// Holds a collection of media files and associated information for either the left or right camera
/// TLC=CMS Version = 1
/// Children:
///     int32: Count of MSI (Media Info) entries
///     TLC: Array of MSI (Media Info) structures
/// </summary>
struct _CMS* EMObsReader::GetCMS() {

    int ret = 0;
    struct _CMS* pCMS = new _CMS();

    if (pCMS != nullptr) {

        pCMS->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pCMS->cTLC, 3);
        reader->GetNextAsFixedChar(&pCMS->cTLCVersion, 1);
        if (memcmp(pCMS->cTLC, "CMS", 3) == 0) {
            if (pCMS->cTLCVersion == 1) {

                pCMS->Count = reader->GetNextAsInt32();
                pCMS->pMSIArray = GetMSIArray(pCMS->Count);
            }
            else {
                printf("***GetCMS Error CMS, unexpected TLC version of %i found\n", (int)pCMS->cTLCVersion);
                delete pCMS;
                pCMS = nullptr;
            }
        }
        else {
            printf("***GetCMS Error CMS expected not found\n");
            hexDump("before TLC:", -1, reader->GetReadPointerPtr(pCMS->fileSeekPointer - 16), 16);
            hexDump("       TLC:", -1, reader->GetReadPointerPtr(pCMS->fileSeekPointer), 16);
            hexDump("after  TLC:", -1, reader->GetReadPointerPtr(pCMS->fileSeekPointer + 16), 16);
            delete pCMS;
            pCMS = nullptr;
        }
    }

    return pCMS;
}


/// <summary>
/// Retrieves an array of MSI (Media Info) structures.
/// </summary>
/// <returns>A pointer to a vector containing pointers to MSI structures, or null if no array is available.</returns>
std::vector<struct _MSI*> EMObsReader::GetMSIArray(int count)
{
    std::vector<struct _MSI*> ret;

    // Resize the vector to the desired dimensions
    ret.resize(count);

    // Optionally, initialize with default values or perform operations
    for (int x = 0; x < count; ++x) {

        // Initialize each element or perform other operations
        ret[x] = GetMSI();
    }

    return ret;

}

/// <summary>
/// Holds and array of MSI (Media Info)
/// TLC=MSI Version = 0
/// Children:
///     wstring: Media File
///     int64: Frame Count
///     double: Frame Rate
/// </summary>
struct _MSI* EMObsReader::GetMSI() {

    int ret = 0;
    struct _MSI* pMSI = new _MSI();

    if (pMSI != nullptr) {

        pMSI->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pMSI->cTLC, 3);
        reader->GetNextAsFixedChar(&pMSI->cTLCVersion, 1);
        if (memcmp(pMSI->cTLC, "MSI", 3) == 0) {
            if (pMSI->cTLCVersion == 0) {

                pMSI->wsMediaFile = reader->GetNextAsWString();
                pMSI->FrameCount = reader->GetNextAsInt64();
                pMSI->FrameRate = reader->GetNextAsDouble();
            }
            else {
                printf("***GetMSI Error MSI, unexpected TLC version of %i found\n", (int)pMSI->cTLCVersion);
                delete pMSI;
                pMSI = nullptr;
            }
        }
        else {
            printf("***GetMSI Error MSI expected not found\n");
            delete pMSI;
            pMSI = nullptr;
        }
    }

    return pMSI;
}


/// <summary>
/// Holds and array of Period PED
/// TLC=PER Version = 1
/// Children:
///     TLC: PED
/// </summary>
struct _PER* EMObsReader::GetPER() {

    int ret = 0;
    struct _PER* pPER = new _PER();

    if (pPER != nullptr) {

        pPER->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPER->cTLC, 3);
        reader->GetNextAsFixedChar(&pPER->cTLCVersion, 1);
        if (memcmp(pPER->cTLC, "PER", 3) == 0) {
            if (pPER->cTLCVersion == 1) {

                pPER->PeriodCount = reader->GetNextAsInt32();
                pPER->pPEDArray = GetPEDArray(pPER->PeriodCount);

            }
            else {
                printf("***GetPER Error PER, unexpected TLC version of %i found\n", (int)pPER->cTLCVersion);
                delete pPER;
                pPER = nullptr;
            }
        }
        else {
            printf("***GetPER Error PER expected not found\n");
            delete pPER;
            pPER = nullptr;
        }
    }

    return pPER;
}

/// <summary>
/// Retrieves an array of PED (single period) structures.
/// </summary>
/// <returns>A pointer to a vector containing pointers to PED structures, or null if no array is available.</returns>
std::vector<struct _PED*> EMObsReader::GetPEDArray(int count)
{
    std::vector<struct _PED*> ret;

    // Resize the vector to the desired dimensions
    ret.resize(count);

    // Optionally, initialize with default values or perform operations
    for (int x = 0; x < count; ++x) {

        // Initialize each element or perform other operations
        ret[x] = GetPED();
    }

    return ret;

}

/// <summary>
/// Holds an array of Period PED
/// TLC=PED Version = 0
/// Children:
///     TLC: FRA
/// </summary>
struct _PED* EMObsReader::GetPED() {

    int ret = 0;
    struct _PED* pPED = new _PED();

    if (pPED != nullptr) {

        pPED->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pPED->cTLC, 3);
        reader->GetNextAsFixedChar(&pPED->cTLCVersion, 1);
        if (memcmp(pPED->cTLC, "PED", 3) == 0) {
            if (pPED->cTLCVersion == 0) {

                pPED->wsPeriodName = reader->GetNextAsWString();
				reader->GetNextAsFixedChar(pPED->bData, sizeof(pPED->bData)/*17*/);

                pPED->pFRAStart = GetFRA();
                pPED->pFRAEnd = GetFRA();
            }
            else {
                printf("***GetPED Error PED, unexpected TLC version of %i found\n", (int)pPED->cTLCVersion);
                delete pPED;
                pPED = nullptr;
            }
        }
        else {
            printf("***GetPER Error PED expected not found\n");
            delete pPED;
            pPED = nullptr;
        }
    }

    return pPED;
}


/// <summary>
/// Holds the .cam calibration data
/// TLC=CCC Version = 0
/// Children:
///     TLC:CAM
/// </summary>
struct _CCC* EMObsReader::GetCCC() {

    int ret = 0;
    struct _CCC* pCCC = new _CCC();

    if (pCCC != nullptr) {

        pCCC->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pCCC->cTLC, 3);
        reader->GetNextAsFixedChar(&pCCC->cTLCVersion, 1);
        if (memcmp(pCCC->cTLC, "CCC", 3) == 0) {
            if (pCCC->cTLCVersion == 0) {

                reader->GetNextAsFixedChar(pCCC->bData1, sizeof(pCCC->bData1));
				pCCC->pCAM = GetCAM();
                reader->GetNextAsFixedChar(pCCC->bData2, sizeof(pCCC->bData2));
				pCCC->frameHeight = reader->GetNextAsInt32();
				pCCC->frameWidth = reader->GetNextAsInt32();
            }
            else {
                printf("***GetCCC Error CCC, unexpected TLC version of %i found\n", (int)pCCC->cTLCVersion);
                delete pCCC;
                pCCC = nullptr;
            }
        }
        else {
            printf("***GetCCC Error CCC expected not found\n");
            delete pCCC;
            pCCC = nullptr;
        }
    }

    return pCCC;
}

/// <summary>
/// Contains camera calibration and other camera related data
/// This structure is exactly what is in a SeaGIS .cam file
/// 
/// TLC=CAM Version = 1
/// Children:
///     wstring:wsCameraName
///     wstring:wsDerviedFrom
///     double: X Pixel Size in mm
///     double: Y Pixel Size in mm
///     long: frameHeight in pixels
///     long: frameWidth in pixels
///     TLC:MAT Calibration data
///     TLC:MAT Unknown data
/// </summary>
struct _CAM* EMObsReader::GetCAM() {

    int ret = 0;
    struct _CAM* pCAM = new _CAM();
	std::vector<std::vector<double>> mat;

    if (pCAM != nullptr) {

        pCAM->fileSeekPointer = reader->GetReadPointer();
        reader->GetNextAsFixedChar(pCAM->cTLC, 3);
        reader->GetNextAsFixedChar(&pCAM->cTLCVersion, 1);
        if (memcmp(pCAM->cTLC, "CAM", 3) == 0) {
            if (pCAM->cTLCVersion == 1) {
				pCAM->wsCameraName = reader->GetNextAsWString();
				pCAM->wsDerivedFrom = reader->GetNextAsWString();
				pCAM->xPixelSize = reader->GetNextAsDouble();
				pCAM->yPixelSize = reader->GetNextAsDouble();
				pCAM->frameHeight = reader->GetNextAsInt32();
				pCAM->frameWidth = reader->GetNextAsInt32();

                // Get the calibration data
				mat = reader->GetNextAsMATdouble();

                pCAM->xPPOffset = mat[0][0];
                pCAM->yPPOffset = mat[1][0];
				pCAM->focalLength = mat[2][0];
				pCAM->k3RadialDistortion = mat[3][0];
                pCAM->k5RadialDistortion = mat[4][0];
                pCAM->k7RadialDistortion = mat[5][0];
				pCAM->p1DecenteringDistortion = mat[6][0];
				pCAM->p2DecenteringDistortion = mat[7][0];
				pCAM->orthogonality = mat[8][0];
				pCAM->affinity = mat[9][0];
				pCAM->cameraX = mat[10][0];
				pCAM->cameraY = mat[11][0];
				pCAM->cameraZ = mat[12][0];
				pCAM->omega = mat[13][0];
                pCAM->phi = mat[14][0];
                pCAM->kappa = mat[15][0];
            }
            else {
                printf("***GetCAM Error CAM, unexpected TLC version of %i found\n", (int)pCAM->cTLCVersion);
                delete pCAM;
                pCAM = nullptr;
            }
        }
        else {
            printf("***GetCAM Error CAM expected not found\n");
            delete pCAM;
            pCAM = nullptr;
        }
    }

    return pCAM;
}

/// <summary>
/// EMOBReaderBase
/// </summary>
/// <param name="_fileSpec"></param>
EMObsReaderBase::EMObsReaderBase(std::string& _fileSpec) : filespec(_fileSpec) {
    readBuffer = nullptr;
}

EMObsReaderBase::~EMObsReaderBase() {
    free(this->readBuffer);
}

int EMObsReaderBase::ReadFile() {
    int ret = 0;

    long size = getFileSize(this->filespec);

    readBuffer = (unsigned char*)malloc(size);

    if (readBuffer != NULL) {
        readBufferSize = size;

        bool ok = readFileIntoBuffer(filespec, readBuffer, readBufferSize);

        if (ok) {
            readPointer = 0;
            seekPointer = 0;
        }
        else
            ret = -1;
    }

    return ret;
}

/// <summary>
/// Return the size of the read buffer
/// </summary>
/// <returns></returns>
size_t EMObsReaderBase::GetSize() {
    return readBufferSize;
}


/// <summary>
/// Looks are the next TLC without moving the read pointer and not using the seek pointer
/// i.e. it is independent of the GetFirstTLC/GetNextTLC
/// </summary>
/// <param name="TLC"></param>
/// <returns>-1 means not a TLC
///          -2 means end of buffer
///           0 means a TLC
/// </returns>
int EMObsReaderBase::PeekNextTLC(char* TLC) {
    int ret = -1;

    // Reset
    TLC[0] = '\0';

    if (readPointer < readBufferSize - 3) {

        if (IsTLC(readPointer, TLC) == true)
            ret = 0;
        else
            ret = -1;
    }
    else
        ret = -2;

    return ret;
}

long EMObsReaderBase::GetReadPointer() {
    return readPointer;
}

void * EMObsReaderBase::GetReadPointerPtr(long pointer) {
    return &readBuffer[pointer];
}


std::wstring EMObsReaderBase::GetNextAsWString() {

    std::wstring ret;

    int32_t stringSize = -GetNextAsInt32();
    wchar_t* p = (wchar_t*)&readBuffer[readPointer];

    std::wstring s(p, stringSize);
    readPointer += stringSize * sizeof(wchar_t);

    return s;
}

std::int64_t EMObsReaderBase::GetNextAsInt64()
{
    int64_t ret = 0;
    int64_t* p = (int64_t*)&readBuffer[readPointer];

    ret = *p;
    readPointer += sizeof(int64_t);

    return ret;
}

std::int32_t EMObsReaderBase::GetNextAsInt32()
{
    int32_t ret = 0;
    int32_t* p = (int32_t*)&readBuffer[readPointer];

    ret = *p;
    readPointer += sizeof(int32_t);

    return ret;
}

std::int16_t EMObsReaderBase::GetNextAsInt16()
{
    int16_t ret = 0;
    int16_t* p = (int16_t*)&readBuffer[readPointer];

    ret = *p;
    readPointer += sizeof(int16_t);

    return ret;
}

char* EMObsReaderBase::GetNextAsFixedChar(char* buffer, size_t len)
{
    memcpy(buffer, &readBuffer[readPointer], len);
    readPointer += (long)len;

    return buffer;
}

float EMObsReaderBase::GetNextAsFloat() {
    float ret = 0;
    float* p = (float*)&readBuffer[readPointer];

    ret = *p;
    readPointer += sizeof(float);

    return ret;
}

double EMObsReaderBase::GetNextAsDouble()
{
    double ret = 0;
    double* p = (double*)&readBuffer[readPointer];

    ret = *p;
    readPointer += sizeof(double);

    return ret;
}

std::vector<std::vector<std::wstring>> EMObsReaderBase::GetNextAsMATwstring()
{
    std::vector<std::vector<std::wstring>> ret;

    // Check this is really a matrix 
    char szMAT[4];
    GetNextAsFixedChar(szMAT, 4);

    if (strcmp(szMAT, "MAT") == 0) {

        int32_t dimX = GetNextAsInt32();
        int32_t dimY = GetNextAsInt32();

        // Resize the vector to the desired dimensions
        ret.resize(dimX);
        for (auto& row : ret) {
            row.resize(dimY);
        }

        // Optionally, initialize with default values or perform operations
        for (int y = 0; y < dimY; ++y) {
            for (int x = 0; x < dimX; ++x) {


                // Initialize each element or perform other operations
                ret[x][y] = GetNextAsWString();
            }
        }
    }

    return ret;
}

std::vector<std::vector<double>> EMObsReaderBase::GetNextAsMATdouble()
{
    std::vector<std::vector<double>> ret;

    // Check this is really a matrix 
    char szMAT[4];
    GetNextAsFixedChar(szMAT, 4);

    if (strcmp(szMAT, "MAT") == 0) {

        int32_t dimX = GetNextAsInt32();
        int32_t dimY = GetNextAsInt32();

        // Resize the vector to the desired dimensions
        ret.resize(dimX);
        for (auto& row : ret) {
            row.resize(dimY);
        }

        // Optionally, initialize with default values or perform operations
        for (int y = 0; y < dimY; ++y) {
            for (int x = 0; x < dimX; ++x) {


                // Initialize each element or perform other operations
                ret[x][y] = GetNextAsDouble();
            }
        }
    }

    return ret;
}


int EMObsReaderBase::GetFirstTLC(void** p, int* size, char* TLC) {

    return GetNextTLC(p, size, TLC);
}


int EMObsReaderBase::GetNextTLC(void** p, int* size, char* TLC) {

    int ret = -1;
    long retPointer;
    long retPointerAfter;

    // Reset    
    *size = 0;

    // Find the next Three Letter Code (TLC)
    // -1 indicates the end
    retPointer = findNextTLC(seekPointer, TLC);

    if (retPointer != -1) {

        lastTLCSeekPointer = retPointer;

        char TLCAfter[4];
        // Find the end of this structure
        retPointerAfter = findNextTLC(retPointer + 3, TLCAfter);
        if (retPointerAfter != -1) {

            *size = (int)(retPointerAfter - retPointer);
            *p = (unsigned char*)&readBuffer[retPointer];

            seekPointer = retPointerAfter;
            ret = 0;
        }
        else {
            // Must be at the end
            *size = (int)(readBufferSize - retPointer);
            *p = (unsigned char*)&readBuffer[retPointer];

            readPointer = seekPointer;
            seekPointer = (long)readBufferSize;
            ret = 0;

        }

    }
    else
        ret = -1;

    return ret;
}


long EMObsReaderBase::GetLastTLCSeekPointer() {

    return lastTLCSeekPointer;
}


/// <summary>
/// The seek point is used for searching i.e. GetFirstTLC()
/// </summary>
void EMObsReaderBase::SetSeekPointerToReadPointer() {

    seekPointer = readPointer;
}


/// <summary>
/// The read point is the position of the logical read through the buffer
/// </summary>
void EMObsReaderBase::SetReadPointerToSeekPointer() {

    readPointer = seekPointer;
}

/// <summary>
/// The lastTLCSeekPointer is the position of the last TLC found using GetFirstTLC/GetNextTLC
/// </summary>
void EMObsReaderBase::SetReadPointerToLastTLCSeekPointer() {

    readPointer = lastTLCSeekPointer;

}


long EMObsReaderBase::getFileSize(const std::string& fileName) {
    std::ifstream file(fileName, std::ifstream::binary | std::ifstream::ate);

    if (file) {
        // Get the position of the file pointer
        long fileSize = (long)file.tellg();
        file.close();
        return fileSize;
    }
    else {
        std::cerr << "Could not open the file '" << fileName << "'" << std::endl;
        return -1; // Error condition
    }
}


bool EMObsReaderBase::readFileIntoBuffer(const std::string& fileName, unsigned char* buffer, size_t bufferSize) {
    std::ifstream file(fileName, std::ifstream::binary);

    if (!file) {
        std::cerr << "Could not open the file '" << fileName << "'" << std::endl;
        return false;
    }

    // Clear the buffer
    std::memset(buffer, 0, bufferSize);

    // Read the entire file into the buffer
    file.read(reinterpret_cast<char*>(buffer), bufferSize);

    if (!file) {
        std::cerr << "Error occurred while reading the file" << std::endl;
        return false;
    }

    file.close();
    return true;
}


// Searches the buffer for a three letter code (TLC)
// TLC should be declared as char TLC[4]
// Return 0 if found or -1 if we have reached the end of the buffer
long EMObsReaderBase::findNextTLC(long startPointer, char* TLC) {
    long ret = -1;
    long findPointer = startPointer;
    bool found = false;

    while (findPointer < readBufferSize && !found) {

        if (IsTLC(findPointer, TLC)) {
            ret = findPointer;
            found = true;
        }
        else
            findPointer++;

    }

    return ret;
}


/// <summary>
/// Check if there is a TLC at the startPointer
/// TLC should be declared as char TLC[4]
/// Look for a three letter code (TLC) where the characters are upper case and the 
/// second and third characters can also be digits. The fourth character must can 
/// be ASCII 0 to 5
/// </summary>
/// <param name="startPointer"></param>
/// <param name="TLC"></param>
/// <returns></returns>
bool EMObsReaderBase::IsTLC(long startPointer, char* TLC) {

    bool ret = false;

    if (startPointer + 3 < readBufferSize) {

        unsigned char* p = (unsigned char*)&readBuffer[startPointer];
        if (isupper((unsigned char)*p) &&
            (isupper((unsigned char)*(p + 1)) || isdigit((unsigned char)*(p + 1))) &&
            (isupper((unsigned char)*(p + 2)) || isdigit((unsigned char)*(p + 2))) &&
            (*(p + 3) >= 0 && *(p + 3) <= 5)) {

            memcpy(TLC, p, 3);
            TLC[3] = '\0';

            ret = true;
        }
    }

    return ret;
}


/// <summary>
/// Find the first string (if any) in the block of memory starting at p
/// </summary>
/// <param name="p"></param>
/// <param name="size"></param>
/// <param name="wssize">Size of the string including the 4 byte size at the start</param>
/// <returns>Index to the start of the string (which is the 4 byte size)</returns>
long EMObsReaderBase::FindFirstwstring(void* p, int size, int* wssize) {

    long ret = -1;
    this->p = (unsigned char*)p;
    this->size = size;
    this->pLast = (unsigned char*)p;

    // Reset
    *wssize = 0;

    ret = FindNextwstring(wssize);

    return ret;
}

long EMObsReaderBase::FindNextwstring(int* wssize) {

    int sizeLeft = this->size - (int)(this->pLast - this->p);

    for (int i = 0; i < sizeLeft - 3; i++) {
        // Minus int32_t values are used to indicate the length of the string
        // So if we see a int32_t that is negative and less than -512 then we have found a string
        // Not perfect and we will miss any zero length strings because int32_t will be a too common
        // signature
        int32_t* pws = (int32_t*)&this->pLast[i];
        if (*pws < 0 && *pws > -512) {
            int sizeFound = -(*pws);

            // Check the supposed string is within the buffer range
            if (i + sizeFound <= sizeLeft) {

                // Typically a string will be 2 bytes per character where only the first byte is used (also not perfect)
                bool allOk = true;
                int16_t* pwsInner = (int16_t*)&this->pLast[i + sizeof(int32_t)];
                for (int j = 0; j < sizeFound; j++) {
                    if (!(pwsInner[j] > 0 && pwsInner[j] < 256)) {
                        allOk = false;
                        break;
                    }
                }

                if (allOk) {
                    // We have found a string
                    *wssize = (sizeFound * sizeof(wchar_t)) + sizeof(int32_t);
                    long ret = i + (long)(this->pLast - this->p);
                    this->pLast += i + *wssize;

                    return ret;
                }
            }
        }
    }

    return -1;
}


int EMObsReaderBase::HexDumpLine(long seek, int dataLength, int widthToDisplay, std::wstring& address, std::wstring& hex, std::wstring& asc) {
    int ret = 0;
    std::wstringstream ss;

    unsigned char* pc = (unsigned char*)readBuffer;
    unsigned char* p = pc + seek;

    // Format the address
    ss.str(L"");
    ss.clear();
    ss << std::setfill(L'0') << std::setw(8) << std::hex << std::uppercase << seek;
    address = ss.str();

    // Format hex section
    ss.str(L"");
    ss.clear();
    int i;
    for (i = 0; i < widthToDisplay; i++) {
        if (i > 0)
            ss << L" ";

        if (i < dataLength)
            ss << std::setfill(L'0') << std::setw(2) << std::hex << std::uppercase << (int)p[i];
        else
            ss << L"  ";
    }
    hex = ss.str();

    // Format the ASCII section
    ss.str(L"");
    ss.clear();
    char c;
    for (i = 0; i < widthToDisplay; i++) {

        if (i < dataLength) {
            c = (int)p[i];
            if ((c < 0x20) || (c > 0x7e))
                ss << L".";
            else
                ss << (wchar_t)c;
        }
        else {
            ss << L" ";
        }

    }
    asc = ss.str();


    return ret;
}



int EMObsReader::ExtractTLCs(std::list<struct _OutputTLC*>& outputTLCsAdd) {
    int ret = 0;
    struct _EBS* pEBS = nullptr;
    std::list<struct _IDA*> IDAList;
    std::list<struct _CMS*> CMSList;    // List of 2 items max
    struct _PER* pPER = nullptr;
    std::list<struct _CCC*> CCCList;    // List of 2 items max
    bool finished = false;

    ret = reader->ReadFile();

    if (ret == 0) {

        // Convert the filespec to a std::filesystem::path object
        fs::path fullPath(filespec);

        // Extract the path (without the filename)
        fs::path directoryPath = fullPath.parent_path();

        // Extract the filename with extension
        fs::path fileNameWithExtension = fullPath.filename();



        int row = 1;

        unsigned char* p;
        int size;
        char TLC[4];
        ret = reader->GetFirstTLC((void**)&p, &size, TLC);

        while (ret == 0 && finished == false) {
            unsigned char* pAfterTLC = (unsigned char*)(p + 3);
            int fixedSize = size;


            std::wstring wideTLC(TLC, TLC + 3);  // Copy first 3 characters

            struct _OutputTLC* outputTLC = new struct _OutputTLC;

            outputTLC->row = row++;
            outputTLC->Path = directoryPath;
            outputTLC->File1 = fileNameWithExtension;
            outputTLC->seekOffset = reader->GetLastTLCSeekPointer();
            outputTLC->tlc = wideTLC;
            outputTLC->cTLCByte = *pAfterTLC;

            // Extract the data (development only)
            if (outputTLC->tlc == L"FRA") {
                reader->SetReadPointerToLastTLCSeekPointer();
                struct _FRA* pFAR = GetFRA();

                if (pFAR != nullptr) {
                    outputTLC->data1 = std::to_wstring(pFAR->iCameraZeroLeftOneRight);
                    outputTLC->data2 = std::to_wstring(pFAR->iFrameIndex);
                }
            }


            outputTLCsAdd.push_back(outputTLC);

            ret = reader->GetNextTLC((void**)&p, &size, TLC);

            if (ret == -1) {
                finished = true;
                ret = 0;
            }
        }
    }


    // Clear
    if (pEBS)
        delete pEBS;

    return ret;
}


int EMObsReader::HexDumpToFile(std::wofstream& outputFileStream, int rowWidth, int rowsPerPage) {

    int ret = 0;
    std::wstring address;
    std::wstring hex;
    std::wstring asc;

    int row = 0;
    int rows = 0;
    int dataLength;

    ret = reader->ReadFile();

    if (ret == 0) {
        size_t size = reader->GetSize();

        for (long seek = 0; seek < size; seek += rowWidth) {

            if (size - seek < 16)
                dataLength = (int)(size - (size_t)seek);
            else
                dataLength = rowWidth;


            ret = reader->HexDumpLine(seek, dataLength, rowWidth, address, hex, asc);
            if (ret == 0) {
                outputFileStream << address << L"  " << hex << L"  " << asc << std::endl;

                row++;
                rows++;
                if (rows == rowsPerPage) {
                    rows = 0;
                    outputFileStream << L"\f";
                }
            }
            else
                outputFileStream << std::endl << L"Error from HexDumpLine = " << ret << " << std::endl";
        }
    }

    return ret;

}


static void ClearOutputRow(struct _SurveyRow* outputRow)
{
    // Manually initialize values
    outputRow->row = 0;
    outputRow->PathEMObs = L"";
    outputRow->FileEMObs = L"";
    outputRow->opCode = L"";
    outputRow->rowType = None;
    outputRow->Period = L"";
    outputRow->Path = L"";
    outputRow->FileL = L"";
    outputRow->FileLStatus = L"";
    outputRow->FrameL = 0;
    outputRow->PointLX1 = 0.0;
    outputRow->PointLY1 = 0.0;
    outputRow->PointLX2 = 0.0;
    outputRow->PointLY2 = 0.0;
    outputRow->FileR = L"";
    outputRow->FileRStatus = L"";
    outputRow->FrameR = 0;
    outputRow->PointRX1 = 0.0;
    outputRow->PointRY1 = 0.0;
    outputRow->PointRX2 = 0.0;
    outputRow->PointRY2 = 0.0;
    outputRow->Length = 0.0;
    outputRow->Family = L"";
    outputRow->Genus = L"";
    outputRow->Species = L"";
    outputRow->count = 0;
}