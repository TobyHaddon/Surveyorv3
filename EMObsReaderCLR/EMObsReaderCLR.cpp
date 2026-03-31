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
        RowTypePoint3D = 2,
        RowTypePoint2DLeftCamera = 3,
        RowTypePoint2DRightCamera = 4
    };

    public ref class OutputRow
    {
    public:
        int row;
        System::String^ PathEMObs;
        System::String^ FileEMObs;
        System::String^ OpCode;
        System::String^ Analyst;
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
            System::String^ _Analyst,
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
			Analyst = _Analyst;
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

    public ref class PeriodRow
    {
    public:
        int row;
        System::String^ PeriodName;
        int Camera;
        System::String^ MediaFile;
        long StartFrame;
        long EndFrame;

        PeriodRow(int row, System::String^ periodName, int camera, System::String^ mediaFile, long startFrame, long endFrame)
        {
            this->row = row;
            PeriodName = periodName;
            Camera = camera;
            MediaFile = mediaFile;
            StartFrame = startFrame;
            EndFrame = endFrame;
        }
    };

    public ref class MediaInfoRow
    {
    public:
        int row;
        bool TrueLeftFalseRightCamera;
        System::String^ MediaFile;
        int FrameCount;
        double FrameRate;

        MediaInfoRow(int row, bool trueLeftFalseRightCamera, System::String^ mediaFile, int frameCount, double frameRate)
        {
            this->row = row;    
            TrueLeftFalseRightCamera = trueLeftFalseRightCamera;
            MediaFile = mediaFile;
            FrameCount = frameCount;
            FrameRate = frameRate;
        }
    };

    public ref class CalibrationRow
    {
    public:
        int row;
        bool TrueLeftFalseRightCamera;
        System::String^ CameraName;
        System::String^ DerivedFrom;

        double XPixelSize;
        double YPixelSize;
        int FrameHeight;
        int FrameWidth;

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

        CalibrationRow(
            int row,
            bool trueLeftFalseRightCamera,
            System::String^ cameraName,
            System::String^ derivedFrom,
            double xPixelSize,
            double yPixelSize,
            int frameHeight,
            int frameWidth,
            double xPPOffset,
            double yPPOffset,
            double focalLength,
            double k3RadialDistortion,
            double k5RadialDistortion,
            double k7RadialDistortion,
            double p1DecenteringDistortion,
            double p2DecenteringDistortion,
            double orthogonality,
            double affinity,
            double cameraX,
            double cameraY,
            double cameraZ,
            double omega,
            double phi,
            double kappa)
        {
			this->row = row;
			TrueLeftFalseRightCamera = trueLeftFalseRightCamera;
            CameraName = cameraName;
            DerivedFrom = derivedFrom;
            XPixelSize = xPixelSize;
            YPixelSize = yPixelSize;
            FrameHeight = frameHeight;
            FrameWidth = frameWidth;
            XPPOffset = xPPOffset;
            YPPOffset = yPPOffset;
            FocalLength = focalLength;
            K3RadialDistortion = k3RadialDistortion;
            K5RadialDistortion = k5RadialDistortion;
            K7RadialDistortion = k7RadialDistortion;
            P1DecenteringDistortion = p1DecenteringDistortion;
            P2DecenteringDistortion = p2DecenteringDistortion;
            Orthogonality = orthogonality;
            Affinity = affinity;
            CameraX = cameraX;
            CameraY = cameraY;
            CameraZ = cameraZ;
            Omega = omega;
            Phi = phi;
            Kappa = kappa;
        }
    };

    public ref class EMObsReaderCLR
    {
    private:
        System::IntPtr nativeReader;

    public:
        EMObsReaderCLR(System::String^ filePath)
        {
            // Convert System::String^ to std::wstring
            std::string nativeFilePath = msclr::interop::marshal_as<std::string>(filePath);
            nativeReader = System::IntPtr(new EMObsReader(nativeFilePath));
        }

        ~EMObsReaderCLR()
        {
            this->!EMObsReaderCLR();
        }

        !EMObsReaderCLR()
        {
            if (nativeReader != System::IntPtr::Zero)
            {
                delete static_cast<EMObsReader*>(nativeReader.ToPointer());
                nativeReader = System::IntPtr::Zero;
            }
        }

        System::Collections::Generic::List<OutputRow^>^ Process()
        {
            auto* reader = static_cast<EMObsReader*>(nativeReader.ToPointer());

            // Native std::list to hold _OutputRow pointers
            std::list<struct _SurveyRow*> outputRows;

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
                case RowType::Point3D:
                    managedRowType = RowTypeManaged::RowTypePoint3D;
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
                    msclr::interop::marshal_as<System::String^>(item->Analyst),
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

       
        System::Collections::Generic::List<PeriodRow^>^ GetPeriodRows()
        {
            auto* reader = static_cast<EMObsReader*>(nativeReader.ToPointer());

            std::list<struct _OutputPeriodRow*> nativeRows;
            reader->GetPeriodRows(nativeRows);

            auto managedRows = gcnew System::Collections::Generic::List<PeriodRow^>();

            for (auto* item : nativeRows) {
                if (item == nullptr) {
                    continue;
                }

                managedRows->Add(gcnew PeriodRow(
                    item->row,
                    msclr::interop::marshal_as<System::String^>(item->PeriodName),
                    item->Camera,
                    msclr::interop::marshal_as<System::String^>(item->MediaFile),
                    item->StartFrame,
                    item->EndFrame));

                delete item; // free native DTO after conversion
            }

            nativeRows.clear();
            return managedRows;
        }  

        System::Collections::Generic::List<MediaInfoRow^>^ GetMediaInfoRows()
        {
            auto* reader = static_cast<EMObsReader*>(nativeReader.ToPointer());

            std::list<struct _OutputMediaInfoRow*> nativeRows;
            reader->GetMediaInfoRows(nativeRows);

            auto managedRows = gcnew System::Collections::Generic::List<MediaInfoRow^>();

            for (auto* item : nativeRows)
            {
                if (item == nullptr)
                    continue;

                managedRows->Add(gcnew MediaInfoRow(
					item->row,
                    item->TrueLeftFalseRightCamera,
                    msclr::interop::marshal_as<System::String^>(item->MediaFile),
                    static_cast<long long>(item->FrameCount),
                    item->FrameRate));

                delete item;
            }

            nativeRows.clear();
            return managedRows;
        }


        System::Collections::Generic::List<CalibrationRow^>^ GetCalibrationRows()
        {
            auto* reader = static_cast<EMObsReader*>(nativeReader.ToPointer());

            std::list<struct _OutputCalibrationRow*> nativeRows;
            reader->GetCalibrationRows(nativeRows);

            auto managedRows = gcnew System::Collections::Generic::List<CalibrationRow^>();

            for (auto* item : nativeRows)
            {
                if (item == nullptr)
                    continue;

                managedRows->Add(gcnew CalibrationRow(
					item->row,
                    item->TrueLeftFalseRightCamera,
                    msclr::interop::marshal_as<System::String^>(item->CameraName),
                    msclr::interop::marshal_as<System::String^>(item->DerivedFrom),
                    item->XPixelSize,
                    item->YPixelSize,
                    item->FrameHeight,
                    item->FrameWidth,
                    item->XPPOffset,
                    item->YPPOffset,
                    item->FocalLength,
                    item->K3RadialDistortion,
                    item->K5RadialDistortion,
                    item->K7RadialDistortion,
                    item->P1DecenteringDistortion,
                    item->P2DecenteringDistortion,
                    item->Orthogonality,
                    item->Affinity,
                    item->CameraX,
                    item->CameraY,
                    item->CameraZ,
                    item->Omega,
                    item->Phi,
                    item->Kappa));

                delete item;
            }

            nativeRows.clear();
            return managedRows;
        }
    };
}
