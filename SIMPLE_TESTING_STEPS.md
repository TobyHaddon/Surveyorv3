# ✅ Simple Testing Steps – Underwater Surveyor

These steps are intended for Microsoft Store certification testers or technical reviewers.

---

## 🧭 Overview

The app allows the user to:
- Open two `.MP4` files from a stereo camera rig
- Sync the videos at a known point (usually a flashlight flash)
- Attach stereo calibration data
- Measure fish length using 3D projection
- Assign species identity to each measurement
- Save survey results for biomass analysis

---

### 🔗 Download Test Files (Public Links)

- 🎥 [Short Test - Left Camera.mp4](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Left%20Camera.mp4)
- 🎥 [Short Test - Right Camera.mp4](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Right%20Camera.mp4)
- 📐 [Short Test - Stereo Calibration.json](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Stereo%20Calibration.json)

---

## 🧪 Step-by-Step Instructions

1. Launch the app
2. Go to **File > New Survey**
3. Browse to the test data downloaded above and multi-select:
   - `Short Test - Left Camera.mp4`
   - `Short Test - Right Camera.mp4`
4. When prompted:
   - **Survey code**: `Short Test`
   - **Depth**: `5`
   - Click **OK**

5. Maximize the app window for best visibility

---

## 🔁 Sync Videos

1. In the **left video**, pause on frame **172** (torch flash frame)
2. In the **right video**, pause on frame **179**
3. Go to **Insert > Lock Media Players**  
   Both players should now sync to one controller

---

## 📷 Load Calibration File

1. Go to **File > Import Calibration**
2. Select `Short Test - Stereo Calibration.json` from the test folder

---

## 🎯 Mark Target Points

1. Advance the synced video to about frame **400**
2. In the **left media player**:
   - Click the center of the hanging board
   - Select **top yellow marker** → red target
   - Select **bottom yellow marker** → green target
3. In the **right media player**:
   - Click corresponding marker points
4. A ✅ tick icon will appear. Click it to confirm the measurement

---

## 🐟 Select Fish Species

- A dialog appears after marking points
- Type `blue`, select a species from dropdown, click **Add**

Markers and dimensions should now appear on both players.

---

## 💾 Save Results

- Go to **File > Save Survey** to save the survey data

---

If any step fails, please note the frame number, selected file, or app dialog state.  
Thank you for reviewing Underwater Surveyor!
