#include "pch.h"

#include <msclr/marshal_cppstd.h>  // For converting String^ to std::wstring

#include "EMObsReaderCLR.h"

#include "..\EMObsReaderCore\EMObsReader.h"

namespace EMObsReaderNameSpace
{
    public enum class RowTypeManaged
    {
        RowTypeNone = 0,
        RowTypeMeasurementPoint3D = 1,
        RowTypePoint3DLeftCamera = 2,
        RowTypePoint3DRightCamera = 3,
        RowTypePoint2DLeftCamera = 4,
        RowTypePoint2DRightCamera = 5
    };

    public ref class OutputRow
    {
    public:
        int row;
        System::String^ PathEMObs;
        System::String^ FileEMObs;
        System::String^ OpCode;
        RowTypeManaged rowType;
        System::String^ Period;
        System::String^ Path;
        System::String^ FileL;
        System::String^ FileLStatus;
        long FrameL;
        double PointLX1;
        double PointLY1;
        double PointLX2;
        double PointLY2;
        System::String^ FileR;
        System::String^ FileRStatus;
        long FrameR;
        double PointRX1;
        double PointRY1;
        double PointRX2;
        double PointRY2;
        double Length;
        System::String^ Family;
        System::String^ Genus;
        System::String^ Species;
        int Count;


        OutputRow(int _row,
            System::String^ _PathEMObs,
            System::String^ _FileEMObs,
            System::String^ _OpCode,
            RowTypeManaged _rowType,
            System::String^ _Period,
            System::String^ _Path,
            System::String^ _FileL,
            System::String^ _FileLStatus,
            long _FrameL,
            double _PointLX1,
            double _PointLY1,
            double _PointLX2,
            double _PointLY2,
            System::String^ _FileR,
            System::String^ _FileRStatus,
            long _FrameR,
            double _PointRX1,
            double _PointRY1,
            double _PointRX2,
            double _PointRY2,
            double _Length,
            System::String^ _Family,
            System::String^ _Genus,
            System::String^ _Species,
            int _Count)
        {
            row = _row;
            PathEMObs = _PathEMObs;
            FileEMObs = _FileEMObs;
            OpCode = _OpCode;
            rowType = _rowType;
            Period = _Period;
            Path = _Path;
            FileL = _FileL;
            FileLStatus = _FileLStatus;
            FrameL = _FrameL;
            PointLX1 = _PointLX1;
            PointLY1 = _PointLY1;
            PointLX2 = _PointLX2;
            PointLY2 = _PointLY2;
            FileR = _FileR;
            FileRStatus = _FileRStatus;
            FrameR = _FrameR;
            PointRX1 = _PointRX1;
            PointRY1 = _PointRY1;
            PointRX2 = _PointRX2;
            PointRY2 = _PointRY2;
            Length = _Length;
            Family = _Family;
            Genus = _Genus;
            Species = _Species;
            Count = _Count;
        }
    };



    public ref class EMObsReaderCLR
    {
    private:
        EMObsReader* reader;  // Native C++ class pointer

    public:
        EMObsReaderCLR(System::String^ filePath)
        {
            // Convert System::String^ to std::wstring
            std::string nativeFilePath = msclr::interop::marshal_as<std::string>(filePath);


            reader = new EMObsReader(nativeFilePath);  // Create an instance of your C++ class
        }

        ~EMObsReaderCLR()
        {
            delete reader;  // Cleanup native instance
        }

        System::Collections::Generic::List<OutputRow^>^ Process()
        {
            // Native std::list to hold _OutputRow pointers
            std::list<struct _OutputRow*> outputRows;

            reader->Process(outputRows);  // Call C++ class method

            // Managed list to hold the converted output rows
            System::Collections::Generic::List<OutputRow^>^ managedOutputRows = gcnew System::Collections::Generic::List<OutputRow^>();

            // Iterate over the native std::list and convert each _OutputRow to a managed OutputRow
            for (auto& item : outputRows)
            {
                // Convert the RowType
                RowTypeManaged managedRowType;
                switch (item->rowType)
                {
                case RowType::None:
                    managedRowType = RowTypeManaged::RowTypeNone;
                    break;
                case RowType::MeasurementPoint3D:
                    managedRowType = RowTypeManaged::RowTypeMeasurementPoint3D;
                    break;
                case RowType::Point3DLeftCamera:
                    managedRowType = RowTypeManaged::RowTypePoint3DLeftCamera;
                    break;
                case RowType::Point3DRightCamera:
                    managedRowType = RowTypeManaged::RowTypePoint3DRightCamera;
                    break;
                case RowType::Point2DLeftCamera:
                    managedRowType = RowTypeManaged::RowTypePoint2DLeftCamera;
                    break;
                case RowType::Point2DRightCamera:
                    managedRowType = RowTypeManaged::RowTypePoint2DRightCamera;
                    break;
                }

                OutputRow^ managedRow = gcnew OutputRow(
                    item->row,
                    msclr::interop::marshal_as<System::String^>(item->PathEMObs),
                    msclr::interop::marshal_as<System::String^>(item->FileEMObs),
                    msclr::interop::marshal_as<System::String^>(item->opCode),
                    managedRowType,
                    msclr::interop::marshal_as<System::String^>(item->Period),
                    msclr::interop::marshal_as<System::String^>(item->Path),
                    msclr::interop::marshal_as<System::String^>(item->FileL),
                    msclr::interop::marshal_as<System::String^>(item->FileLStatus),
                    item->FrameL,
                    item->PointLX1,
                    item->PointLY1,
                    item->PointLX2,
                    item->PointLY2,
                    msclr::interop::marshal_as<System::String^>(item->FileR),
                    msclr::interop::marshal_as<System::String^>(item->FileRStatus),
                    item->FrameR,
                    item->PointRX1,
                    item->PointRY1,
                    item->PointRX2,
                    item->PointRY2,
                    item->Length,
                    msclr::interop::marshal_as<System::String^>(item->Family),
                    msclr::interop::marshal_as<System::String^>(item->Genus),
                    msclr::interop::marshal_as<System::String^>(item->Species),
                    item->count);

                managedOutputRows->Add(managedRow);

                // Optionally clean up the native row after copying data
                delete item;
            }

            return managedOutputRows;
        }
    };
}
