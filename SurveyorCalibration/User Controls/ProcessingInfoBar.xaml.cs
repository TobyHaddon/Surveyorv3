using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;

namespace Surveyor.User_Controls
{
    public sealed partial class ProcessingInfoBar : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProcessingInfoBar), new PropertyMetadata("Processing"));

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(ProcessingInfoBar), new PropertyMetadata("Please wait..."));

        public static readonly DependencyProperty CancelButtonProperty =
            DependencyProperty.Register(nameof(CancelButton), typeof(bool), typeof(ProcessingInfoBar), new PropertyMetadata(false));

        public static readonly DependencyProperty CancelButtonTextProperty =
            DependencyProperty.Register(nameof(CancelButtonText), typeof(string), typeof(ProcessingInfoBar), new PropertyMetadata("Cancel"));

        public static readonly DependencyProperty ElaspedTimeProperty =
            DependencyProperty.Register(nameof(ElaspedTime), typeof(bool), typeof(ProcessingInfoBar), new PropertyMetadata(true));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
        public bool CancelButton { get => (bool)GetValue(CancelButtonProperty); set => SetValue(CancelButtonProperty, value); }
        public string CancelButtonText { get => (string)GetValue(CancelButtonTextProperty); set => SetValue(CancelButtonTextProperty, value); }
        public bool ElaspedTime { get => (bool)GetValue(ElaspedTimeProperty); set => SetValue(ElaspedTimeProperty, value); }

        public event RoutedEventHandler? CancelButton_Click;

        private DispatcherTimer? _timer;
        private DateTimeOffset _start;
        private string? _savedMessage;
        private bool? _savedElapsedFlag;

        private TextBlock? elapsedTextBlock = null;
        private ProgressRing? progressRing = null;

        public ProcessingInfoBar()
        {
            this.InitializeComponent(); // FIX: must be in a sealed partial class matching x:Class
        }


        /// <summary>
        /// You can setup give the ProcessingInfoBar references to the elapsed 
        /// time TextBlock and ProgressRing controls somewhere else in your UI.
        /// The ProcessingInfoBar will update these controls when showing/hiding 
        /// processing state and displaying elapsed time.
        /// You can only supply a TextBlock, only a ProgressRing, or both.
        /// </summary>
        /// <param name="_elapsedTextBlock"></param>
        /// <param name="_progressRing"></param>
        public void WireUpElapsedTimeUIControl(TextBlock? _elapsedTextBlock, ProgressRing? _progressRing)
        {
            elapsedTextBlock = _elapsedTextBlock;
            progressRing = _progressRing;

            // Bring into sync with current state
            if (progressRing is not null)
            {
                progressRing.IsActive = InfoBar.IsOpen;
            }
            if (elapsedTextBlock is not null)
            {
                if (!InfoBar.IsOpen)
                {
                    elapsedTextBlock.Text = string.Empty;
                }
            }
        }


        // Show default (use existing Message)
        public void ShowProcessing() =>
            StartProcessing(keepMessage: true, elapsedOverride: null, messageOverride: null);

        // Show with message override
        public void ShowProcessing(string message) =>
            StartProcessing(keepMessage: false, elapsedOverride: null, messageOverride: message);

        // Show with elapsed flag override (keep message)
        public void ShowProcessing(bool elapsed) =>
            StartProcessing(keepMessage: true, elapsedOverride: elapsed, messageOverride: null);

        // Show with message + elapsed override
        public void ShowProcessing(string message, bool elapsed) =>
            StartProcessing(keepMessage: false, elapsedOverride: elapsed, messageOverride: message);

        // Update the Message while the InfoBar is already open (thread-safe). Does not affect saved original message.
        public void UpdateMessage(string message)
        {
            void Apply() => Message = message ?? string.Empty;

            if (DispatcherQueue?.HasThreadAccess == true)
            {
                Apply();
            }
            else
            {
                _ = DispatcherQueue?.TryEnqueue(Apply);
            }
        }

        // Hide and restore
        public void HideProcessing()
        {
            StopTimer();

            // Remove progressing ring if necessary
            if (progressRing is not null)
                progressRing.IsActive = false;

            // Remove the elapsed time if necessary
            if (elapsedTextBlock is not null)
                elapsedTextBlock.Text = string.Empty;

            if (_savedMessage is not null)
            {
                Message = _savedMessage;
                _savedMessage = null;
            }
            if (_savedElapsedFlag is not null)
            {
                ElaspedTime = _savedElapsedFlag.Value;
                _savedElapsedFlag = null;
            }

            InfoBar.IsOpen = false;
        }


        /// <summary>
        /// Pass through so users of this control can check if it's open
        /// </summary>
        public bool IsOpen { get => InfoBar.IsOpen; }

        /// 
        /// PRIVATE
        /// 

        private void StartProcessing(bool keepMessage, bool? elapsedOverride, string? messageOverride)
        {
            if (!keepMessage && messageOverride is not null)
            {
                _savedMessage ??= Message;
                Message = messageOverride;
            }
            if (elapsedOverride is not null)
            {
                _savedElapsedFlag ??= ElaspedTime;
                ElaspedTime = elapsedOverride.Value;
            }

            InfoBar.IsOpen = true;
            if (progressRing is not null)
                progressRing.IsActive = true;

            if (elapsedTextBlock is not null)
            {
                if (ElaspedTime)
                {
                    _start = DateTimeOffset.Now;
                    EnsureTimer();
                    _timer!.Start();
                }
                else
                {
                    StopTimer();
                    elapsedTextBlock.Text = string.Empty;
                }
            }
        }

        private void EnsureTimer()
        {
            if (elapsedTextBlock is null) return;

            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, __) =>
            {
                var elapsed = DateTimeOffset.Now - _start;
                // HH:MM:SS
                elapsedTextBlock.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            };
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
            CancelButton_Click?.Invoke(this, e);
    }
}