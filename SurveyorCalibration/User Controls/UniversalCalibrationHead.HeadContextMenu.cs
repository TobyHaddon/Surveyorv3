// Contains the high level calibration workflow method
// 
using ColorCode.Common;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SurveyorCalibrationData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Surveyor.Controls
{
    public sealed partial class UniversalCalibrationHead : UserControl
    {
        // Context menu status
        private bool headContextMenuOpen = false;
        private bool? headContextMenuOpenLeftOrRight = null;

        // Callback up to MainWindow to allow the manual bits
        // inside BestFrame.BestFrameReason can be set
        public delegate BestFrame? ToggleManualReasonCallbackDelegate(HeadType headType, int frameSetIndex, BestFrameReason reasonBit);

        public event ToggleManualReasonCallbackDelegate? ToggleManualReasonCallback;

        // Callback up to MainWindow to allow removal of all
        // manually added or manually ignored bits in the best frame.
        public delegate void RemoveAllCallbackDelegate(HeadType headType, BestFrameReason reasonBit);

        public event RemoveAllCallbackDelegate? RemoveAllCallback;


        ///
        /// EVENTS
        ///


        /// <summary>
        /// Called when the user right clicks on the container holding
        /// the image and the sensor coverage canvas
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OverlayContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (headContextMenuOpen)
                return;

            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            if (e.GetCurrentPoint((UIElement)sender).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
                return;

            DisplayHeadContextMenu(sender, e);
            e.Handled = true;
        }


        /// <summary>
        /// Called when the user right clicks on the metadata
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MetaData_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (headContextMenuOpen)
                return;

            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                return;

            if (e.GetCurrentPoint((UIElement)sender).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
                return;

            DisplayHeadContextMenu(sender, e);
            e.Handled = true;
        }


        /// <summary>
        /// Indicate the HeadContextMenu is open 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HeadContextMenu_Closing(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase sender, Microsoft.UI.Xaml.Controls.Primitives.FlyoutBaseClosingEventArgs args)
        {
            headContextMenuOpen = false;
            headContextMenuOpenLeftOrRight = null;
        }


        /// <summary>
        /// Called when the user clicks "Include Frame"  in the context menu. This will toggle the
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuIncludeFrame_Click(object sender, RoutedEventArgs e)
        {
            ToggleReasonBitAndUpdateUI(BestFrameReason.ManuallyAdded);
        }


        /// <summary>
        /// Called when the user clicks "Exclude Frame"  in the context menu. This will toggle the
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuExcludeFrame_Click(object sender, RoutedEventArgs e)
        {
            ToggleReasonBitAndUpdateUI(BestFrameReason.ManuallyIgnored);
        }


        /// <summary>
        /// Remove all the 'Added' entries in the ManuallyAddedIgnoreFrameIndexes 
        /// List<>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuRemoveAllIncludedFrames_Click(object sender, RoutedEventArgs e)
        {
            RemoveAllReasonBitFromBestFrameIndexes(BestFrameReason.ManuallyAdded);
        }


        /// <summary>
        /// Remove all the 'Ignored' entries in the ManuallyAddedIgnoreFrameIndexes 
        /// List<>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuRemoveAllExcludedFrames_Click(object sender, RoutedEventArgs e)
        {
            RemoveAllReasonBitFromBestFrameIndexes(BestFrameReason.ManuallyIgnored);
        }


        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Called to display the Head Canvas Context Menu and enable/disable each menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DisplayHeadContextMenu(object sender, PointerRoutedEventArgs e)
        {
            // Guard
            if (headType is null)
                return;

            // If stereo are we left or right
            //???bool? trueLeftFalseRightContextMenu = null;
            if (IsHeadStereo())
            {
                if (sender is Grid grid)
                {
                    if (grid == LeftOverlayContainer)
                        headContextMenuOpenLeftOrRight = true;
                    else if (grid == RightOverlayContainer)
                        headContextMenuOpenLeftOrRight = false;
                }
                if (sender is Border border)
                {
                    if (border == LeftFrameMetadataBorder)
                        headContextMenuOpenLeftOrRight = true;
                    else if (border == RightFrameMetadataBorder)
                        headContextMenuOpenLeftOrRight = false;
                }
            }
            else
            {
                // Mono is left by definition (even if it's right mono)
                headContextMenuOpenLeftOrRight = true;
            }

            // Get the current frame data
            if (headContextMenuOpenLeftOrRight is not null)
            {
                int frameSetIndex = GetCurrentFrameSetIndex((bool)headContextMenuOpenLeftOrRight);

                // Get the best frame list for the current head type using the callback
                List<BestFrame>? bestFramesList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                if (bestFramesList is not null)
                {
                    // See if there are any manual attributes for this frame
                    BestFrame? manualEntry = bestFramesList
                                                .FirstOrDefault(bf => bf.FrameIndex == frameSetIndex);

                    if (manualEntry is not null)
                    {
//???                        if ((manualEntry.Reason & (BestFrameReason.ManuallyAdded | BestFrameReason.ManuallyAdded)) != 0)
//???                        {
                            // Is manually included flag set
                            if ((manualEntry.Reason & BestFrameReason.ManuallyAdded) != 0)
                                HeadContextMenuIncludeFrame.IsChecked = true;
                            else
                                HeadContextMenuIncludeFrame.IsChecked = false;

                            // Is manually excluded flag set
                            if ((manualEntry.Reason & BestFrameReason.ManuallyIgnored) != 0)
                                HeadContextMenuExcludeFrame.IsChecked = true;
                            else
                                HeadContextMenuExcludeFrame.IsChecked = false;
                        //???                        }
                    }
                    else
                    {
                        HeadContextMenuIncludeFrame.IsChecked = false;
                        HeadContextMenuExcludeFrame.IsChecked = false;
                    }
                }

                // Show the context menu
                var menuFlyout = (MenuFlyout)this.Resources["HeadContextMenu"];
                menuFlyout.ShowAt(sender as FrameworkElement, new FlyoutShowOptions
                {
                    Position = e.GetCurrentPoint(sender as FrameworkElement).Position,
                    Placement = FlyoutPlacementMode.RightEdgeAlignedTop
                });

                // Mark as context menu open
                headContextMenuOpen = true;
            }
        }


        /// <summary>
        /// Toggle the indicates BestFrame Reason bit and update the metadata 
        /// panel and the media timeline display
        /// </summary>
        /// <param name="reasonBit"></param>
        private void ToggleReasonBitAndUpdateUI(BestFrameReason reasonBit)
        {
            if (headContextMenuOpenLeftOrRight is not null)
            {
                BestFrame? updatedReason;
                updatedReason = ToggleManuallyReasonBit((bool)headContextMenuOpenLeftOrRight,
                                                        reasonBit);

                // Update the meta data panel
                if (updatedReason is not null)
                {
                    UpdateFrameMetaDataReason(
                                    (bool)headContextMenuOpenLeftOrRight,
                                    updatedReason.Reason);

                    // Update the media timeline display
                    if ((bool)headContextMenuOpenLeftOrRight)
                        MediaTimeLineDisplayLeft.RenderBestFrameFoundAtOnTimeline(updatedReason);
                    else
                        MediaTimeLineDisplayRight.RenderBestFrameFoundAtOnTimeline(updatedReason);
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <returns></returns>
        private int GetCurrentFrameSetIndex(bool trueLeftFalseRight)
        {
            // Get the current left and right frame indexes (same value in mono)
            (int frameIndexLeft, int frameIndexRight) = GetCurrentFrameIndexes();

            int frameSetIndex = -1;

            if (isLocked)
            {
                // Get the frame set index for the current left and right frame indexes
                frameSetIndex = calibrationStereoFrameSet.GetFrameSetIndexFromLeftRightIndexes(frameIndexLeft, frameIndexRight);
            }
            else if (trueLeftFalseRight == true)
            {
                frameSetIndex = frameIndexLeft;
            }
            else if (trueLeftFalseRight == false)
            {
                frameSetIndex = frameIndexRight;
            }
            return frameSetIndex;

        }


        /// <summary>
        /// Update or add to the ManuallyAddedIgnoreFrameIndexes List<>
        /// </summary>
        /// <param name="trueLeftFalseRight"></param>
        /// <param name="FrameSetIndex"></param>
        /// <param name="reasonBit"></param>
        /// <param name="add"></param>
        /// <returns>Updated BestFrame</returns>
        private BestFrame? ToggleManuallyReasonBit(bool trueLeftFalseRight, BestFrameReason reasonBit)
        {
            BestFrame? updatedBestFrame = null;

            // Guard
            if (headType is null)
                return null;

            int frameSetIndex = GetCurrentFrameSetIndex(trueLeftFalseRight);

            if (frameSetIndex != -1)
            {
                if (ToggleManualReasonCallback is not null && headType is not null)
                {
                    // Call the callback which can be used to add/modify/remove the
                    // manual best frame reason bit
                    updatedBestFrame = ToggleManualReasonCallback?.Invoke((HeadType)headType, frameSetIndex, reasonBit);
                }
            }

            return updatedBestFrame;
        }

        /// <summary>
        /// Reverse loop through the Best frame  list
        /// and remove any entries with the indicate reason bit set in the
        /// Reason field. Then update the metadata panel and media timeline
        /// display for any removed entries.
        /// </summary>
        /// <param name="reasonBit"></param>
        private void RemoveAllReasonBitFromBestFrameIndexes(BestFrameReason reasonBit)
        {
            if (headContextMenuOpenLeftOrRight is not null)
            {
                if (RemoveAllCallback is not null && headType is not null)
                {
                    // Call the callback which can be used to remove all either
                    // manually added or manually ignored best frame reason bit
                    RemoveAllCallback.Invoke((HeadType)headType, reasonBit);
                }

                // If the current frame was affected then update the frame meta
                // data
                // Nasty workaround. Read the existing textual value in the 
                // LeftCalibrationFrameStatusManual/RightCalibrationFrameStatusManual
                // TextBlock to determine if the current displayed frame has the manually
                // added or manually ignored bit set. If we cleared all bits of that
                // type then we need to update the metadata panel to reflect that for
                // the current frame you give the user the correct feedback
                TextBlock calibrationFrameStatusManual;
                
                if ((bool)headContextMenuOpenLeftOrRight)
                    calibrationFrameStatusManual = LeftCalibrationFrameStatusManual;
                else
                    calibrationFrameStatusManual = RightCalibrationFrameStatusManual;

                string currentManualText = calibrationFrameStatusManual.Text;

                if (currentManualText == ManuallyIgnoredText && reasonBit == BestFrameReason.ManuallyIgnored)
                {
                    calibrationFrameStatusManual.Text = string.Empty;
                    calibrationFrameStatusManual.Visibility = Visibility.Collapsed;
                }
                else if (currentManualText == ManuallyAddedText && reasonBit == BestFrameReason.ManuallyAdded)
                {
                    calibrationFrameStatusManual.Text = string.Empty;
                    calibrationFrameStatusManual.Visibility = Visibility.Collapsed;
                }

                // Get the updated best frame list
                List<BestFrame>? bestFrameList = null;
                if (GetBestFrameListCallback is not null && headType is not null)
                {
                    bestFrameList = GetBestFrameListCallback?.Invoke((HeadType)headType);

                    if (bestFrameList is not null)
                    {
                        // Redraw the media timeline display 
                        if ((bool)headContextMenuOpenLeftOrRight)
                        {
                            MediaTimeLineDisplayLeft.RenderBestFramesOnTimeline(bestFrameList);
                            //???MediaTimeLineDisplayLeft.DrawBestFrames();
                        }
                        else
                        {
                            MediaTimeLineDisplayRight.RenderBestFramesOnTimeline(bestFrameList);
                            //???MediaTimeLineDisplayRight.DrawBestFrames();
                        }
                    }
                }

                // Remove from the sensor coverage window
                RenderSensorCoverage();
            }
        }
    }
}