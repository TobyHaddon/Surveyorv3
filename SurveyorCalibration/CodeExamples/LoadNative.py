import tempfile
import numpy as np


#
# Get the encoding video properties
#
def get_video_encoding_properties(file_path):
    """
    Reads the encoding properties of an MP4 file using OpenCV and FFmpeg.

    :param file_path: Path to the MP4 file
    :return: Dictionary containing encoding properties
    """
    # Open video file with OpenCV
    cap = cv2.VideoCapture(file_path)