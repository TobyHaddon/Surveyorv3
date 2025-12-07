#include "readCalibParameters.h"
#include "fstream"
#include "json.hpp"

// Read Calib Camera Calibrator parameters into OpenCV datastructures, suitable
// for further use with OpenCV function. Uses nlohmann's single-header "JSON for
// Modern C++" for JSON import. See https://github.com/nlohmann/json
//
// (c) Calib.io ApS, Public domain

bool readCalibParameters(const std::string& filePath,
    std::vector<cv::Matx33d>& K,
    std::vector<cv::Vec<double, 14>>& k,
    std::vector<cv::Vec3d>& cam_rvecs,
    std::vector<cv::Vec3d>& cam_tvecs) {

    std::ifstream fileStream(filePath);
    nlohmann::json jsonStruct;
    try {
        fileStream >> jsonStruct;
    }
    catch (...) {
        return false;
    }

    assert(jsonStruct["Calibration"]["cameras"][0]["model"]["polymorphic_name"] ==
        "libCalib::CameraModelOpenCV");

    int nCameras = jsonStruct["Calibration"]["cameras"].size();

    if (nCameras < 1) {
        return false;
    }

    K.resize(nCameras);
    k.resize(nCameras);

    cam_rvecs.resize(nCameras);
    cam_tvecs.resize(nCameras);