// Contains the high level calibration workflow method
// 
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
        /// withRemove all the 'Added' entries in the ManuallyAddedIgnoreFrameIndexes 
        /// List<>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuRemoveAllIncludedFrames_Click(object sender, RoutedEventArgs e)
        {
            RemoveAllReasonBitFromManuallyAddedIgnoreFrameIndexes(BestFrameReason.ManuallyAdded);
        }


        /// <summary>
        /// withRemove all the 'Ignored' entries in the ManuallyAddedIgnoreFrameIndexes 
        /// List<>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HeadContextMenuRemoveAllExcludedFrames_Click(object sender, RoutedEventArgs e)
        {
            RemoveAllReasonBitFromManuallyAddedIgnoreFrameIndexes(BestFrameReason.ManuallyIgnored);
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
            headContextMenuOpenLeftOrRight = null;

            // If stereo are we left or right
            bool? trueLeftFalseRightContextMenu = null;
            if (IsHeadStereo())
            {
                if (sender is Grid grid)
                {
                    if (grid == LeftOverlayContainer)
                        trueLeftFalseRightContextMenu = true;
                    else if (grid == RightOverlayContainer)
                        trueLeftFalseRightContextMenu = false;
                }
                if (sender is Border border)
                {
                    if (border == LeftFrameMetadataBorder)
                        trueLeftFalseRightContextMenu = true;
                    else if (border == RightFrameMetadataBorder)
                        trueLeftFalseRightContextMenu = false;
                }
            }
            else
            {
                // Mono is left by definition (even if it's right mono)
                trueLeftFalseRightContextMenu = true;
            }

            // Get the current frame data
            if (trueLeftFalseRightContextMenu is not null)
            {
                headContextMenuOpenLeftOrRight = trueLeftFalseRightContextMenu;

                int frameSetIndex = GetCurrentFrameSetIndex(trueLeftFalseRightContextMenu.Value);

                // See if this is in the ManuallyAddedIgnoreFrameIndexes list
                BestFrame? manualEntry = calibrationStereoFrameSet.Data.BestFrameIndexes
                                            .FirstOrDefault(bf => bf.FrameIndex == frameSetIndex);

                // Proceed if there is a manual entry for this frame index set
                // (if not the menu will just show with both Include and Exclude options un-checked)
                if (manualEntry is not null)
                {
                    if (trueLeftFalseRightContextMenu == true)
                    {
                        headContextMenuOpenLeftOrRight = true;
                    }
                    else if (trueLeftFalseRightContextMenu == false)
                    {
                        headContextMenuOpenLeftOrRight = false;
                    }

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
                }
                else
                {
                    HeadContextMenuIncludeFrame.IsChecked = false;
                    HeadContextMenuExcludeFrame.IsChecked = false;
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
                    UpdateFrameMetaDataReason((bool)headContextMenuOpenLeftOrRight,
                                          null/*bestReason*/,
                                          updatedReason.Reason,
                                          updateOnly: true);

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
            BestFrame? manualFrame = null;

            int frameSetIndex = GetCurrentFrameSetIndex(trueLeftFalseRight);

            if (frameSetIndex != -1)
            {
                // Search ManuallyAddedIgnoreFrameIndexes for a instance where
                // BestFrame.FrameIndex == frameSetIndex. If found, update the Reason bit according to add or remove.
                int? index = calibrationStereoFrameSet.Data.BestFrameIndexes
                    .Select((bf, i) => (bf, i))
                    .Where(x => x.bf.FrameIndex == frameSetIndex)
                    .Select(x => (int?)x.i)
                    .FirstOrDefault();

                // Update existing entry
                if (index is not null)
                {

                    BestFrameReason newBestFrameReason = calibrationStereoFrameSet.Data.BestFrameIndexes[(int)index].Reason;
                    if ((newBestFrameReason & reasonBit) == 0)
                        // Set the bit (and remove all others)
                        newBestFrameReason = reasonBit;
                    else
                        // Reset the bit
                        newBestFrameReason &= ~reasonBit;

                    manualFrame = new(frameSetIndex, newBestFrameReason);
                    calibrationStereoFrameSet.Data.BestFrameIndexes[(int)index] = manualFrame;
                }
                else
                // Create new entry (if necessary)
                {
                    // No record found so must be an add
                    BestFrameReason newBestFrameReason = reasonBit;
                    manualFrame = new(frameSetIndex, newBestFrameReason);
                    calibrationStereoFrameSet.Data.BestFrameIndexes.Add(manualFrame);
                }
            }

            return manualFrame;
        }

        /// <summary>
        /// Reverse loop through the ManuallyAddedIgnoreFrameIndexes list
        /// and remove any entries with the indicate reason bit set in the
        /// Reason field. Then update the metadata panel and media timeline
        /// display for any removed entries.
        /// </summary>
        /// <param name="reasonBit"></param>
        private void RemoveAllReasonBitFromManuallyAddedIgnoreFrameIndexes(BestFrameReason reasonBit)
        {
            List<BestFrame> ManualList = calibrationStereoFrameSet.Data.ManuallyAddedIgnoreFrameIndexes;

            for (int i = ManualList.Count - 1;
                 i >= 0;
                 i--)
            {
                if ((ManualList[i].Reason & reasonBit) != 0)
                {
                    if (headContextMenuOpenLeftOrRight is not null)
                    {
                        BestFrame updatedReason = new(ManualList[i].FrameIndex, BestFrameReason.None);

                        UpdateFrameMetaDataReason((bool)headContextMenuOpenLeftOrRight,
                                                  null/*bestReason*/,
                                                  updatedReason.Reason,
                                                  updateOnly: true);

                        // Update the media timeline display
                        if ((bool)headContextMenuOpenLeftOrRight)
                            MediaTimeLineDisplayLeft.RenderBestFrameFoundAtOnTimeline(updatedReason);
                        else
                            MediaTimeLineDisplayRight.RenderBestFrameFoundAtOnTimeline(updatedReason);
                    }

                    // Remove front the master list
                    ManualList.Remove(ManualList[i]);
                }
            }
        }
    }
}