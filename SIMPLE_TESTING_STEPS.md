# ✅ Simple Testing Steps – Underwater Surveyor

These steps are intended for Microsoft Store certification testers or technical reviewers.

---

## 🧭 Overview

The app allows the user to:
- Open two `.MP4` files from a stereo camera rig
- Sync the videos at a known point (usually a flashlight flash)
- Attach stereo calibration data
- Measure fish length using stereo projection
- Assign species identity to each measurement
- Save survey results 

---

### 🔗 Download Test Files (Public Links)

- 🎥 [Short Test - Left Camera.mp4](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Left%20Camera.mp4)
- 🎥 [Short Test - Right Camera.mp4](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Right%20Camera.mp4)
- 📐 [Short Test - Stereo Calibration.json](https://raw.githubusercontent.com/TobyHaddon/Surveyorv3/master/Surveyorv3/Test%20Data/Short%20Test%20-%20Stereo%20Calibration.json)
_(Right-click the link and choose “Save link as…” to download.)_
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

1. In the **left video**, go to rame **16** (the first flashlight flash frame)
   Best way to do this is to click the frame forward button 16 times or click on the frame index number and enter 16
2. In the **right video**, pause on frame **17**
3. Go to **Insert > Lock Media Players**  
   Both players should now sync to one controller

---

## 📷 Load Calibration File

1. Go to **File > Import Calibration**
2. Browse to the test data downloaded in the earlier step
3. Select `Short Test - Stereo Calibration.json` 

---

## 🎯 Mark Target Points

1. Advance the synced video to about frame **130**
2. In the **left media player**:
   - Click the center of the left hanging board
   - Select **top yellow marker** → red target
   - Select **bottom yellow marker** → green target
3. In the **right media player**:
   - Click the center of the left hanging board
   - Click corresponding marker points
5. A ✅ tick icon will appear in the top of the right magnifier window. Click it to confirm the measurement

---

## 🐟 Select Fish Species

- A dialog appears after marking points
- Type `blue` for example, select a species from dropdown, click **Add**

Markers, dimensions and species should now appear on both players.

---

## 💾 Save Results

- Go to **File > Save Survey** to save the survey data

---

If any step fails, please note the frame number, selected file, or app dialog state.  
Thank you for reviewing Underwater Surveyor!
