using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using System.Diagnostics;
using static Surveyor.User_Controls.Reporter;

namespace Surveyor.User_Controls
{
    public class ReporterListViewItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private WarningLevel _warningLevel;
        private BitmapImage? _imageData;
        private string? _time;
        private string? _channel;
        private string? _message;

        public WarningLevel WarningLevel
        {
            get => _warningLevel;
            set
            {
                _warningLevel = value;
                OnPropertyChanged();
            }
        }

        public BitmapImage? ImageData
        {
            get => _imageData;
            set
            {
                _imageData = value;
                OnPropertyChanged();
            }
        }

        public string? Time
        {
            get => _time;
            set
            {
                _time = value;
                OnPropertyChanged();
            }
        }

        public string? Channel
        {
            get => _channel;
            set
            {
                _channel = value;
                OnPropertyChanged();
            }
        }

        public string? Message
        {
            get => _message;
            set
            {
                _message = value;
                OnPropertyChanged();
            }
        }

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed partial class Reporter : UserControl
    {
        public enum WarningLevel { Error, Warning, Info, Status, Debug, None };

        public ObservableCollection<ReporterListViewItem> ReportItems { get; set; }

        private readonly BitmapImage? imageDataInfo;
        private readonly BitmapImage? imageDataWarning;
        private readonly BitmapImage? imageDataError;
        private readonly BitmapImage? imageDataDebug;

        private bool bDirty;
        private readonly int maxListViewItems;
        private TextBox? textBoxStatus;
        private TextBox? textBoxWarning;
        private TextBox? textBoxError;
        private int warningCount;
        private int errorCount;
        private ReporterListViewItem? itemLastStatus;
        private DispatcherQueue? dispatcherQueue;

        public Reporter()
        {
            InitializeComponent();

            bDirty = false;
            maxListViewItems = 1000;
            warningCount = 0;
            errorCount = 0;
            ReportItems = new ObservableCollection<ReporterListViewItem>();

            imageDataInfo = new BitmapImage(new Uri($"ms-appx:///Assets/rptinfo.png", UriKind.Absolute));
            imageDataWarning = new BitmapImage(new Uri($"ms-appx:///Assets/rptwarn.png", UriKind.Absolute));
            imageDataError = new BitmapImage(new Uri($"ms-appx:///Assets/rpterr.png", UriKind.Absolute));
            imageDataDebug = new BitmapImage(new Uri($"ms-appx:///Assets/rptdbg.png", UriKind.Absolute));

            ListViewReporter.ItemsSource = ReportItems;

            // Clear (rename) any existing reporter.txt file
            try
            {
                string folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                string filePathCurrent = Path.Combine(folderPath, "reporter.txt");
                string filePathPrevious1 = Path.Combine(folderPath, "reporter1.txt");
                string filePathPrevious2 = Path.Combine(folderPath, "reporter2.txt");
                string filePathPrevious3 = Path.Combine(folderPath, "reporter3.txt");

                // Delete reporter3.txt if it exists
                if (File.Exists(filePathPrevious3))
                {
                    File.Delete(filePathPrevious3);
                }

                // Rename reporter2.txt reporter3.txt if reporter2.txt exists
                if (File.Exists(filePathPrevious2))
                {
                    File.Move(filePathPrevious2, filePathPrevious3);
                }

                // Rename reporter1.txt reporter2.txt if reporter1.txt exists
                if (File.Exists(filePathPrevious1))
                {
                    File.Move(filePathPrevious1, filePathPrevious2);
                }

                // Rename reporter.txt reporter1.txt if reporter.txt exists
                if (File.Exists(filePathCurrent))
                {
                    File.Move(filePathCurrent, filePathPrevious1);
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reporter Exception: {ex.Message}");
                // Fail silently or log, depending on your logging setup
            }
            finally
            {
                // Always clear the list at the end
                Clear();
            }

        }


        /// <summary>
        /// Dump the report lines to report.txt
        /// </summary>
        internal void Save()
        {
            try
            {
                string folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                string filePathCurrent = Path.Combine(folderPath, "reporter.txt");

                if (IsDirty())
                {
                    // Write the new report lines to reporter.txt
                    using var writer = new StreamWriter(filePathCurrent);

                    foreach (var item in ReportItems)
                    {
                        // Clean fields: replace tabs/newlines to avoid corrupting the TSV structure
                        string Clean(string? text) =>
                            text?.Replace("\t", " ").Replace("\n", " ").Replace("\r", " ") ?? string.Empty;

                        string level = item.WarningLevel.ToString();
                        string time = Clean(item.Time);
                        string channel = Clean(item.Channel);
                        string message = Clean(item.Message);

                        writer.WriteLine($"{level}\t{time}\t{channel}\t{message}");
                    }

                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reporter.Unload Exception: {ex.Message}");
                // Fail silently or log, depending on your logging setup
            }
            finally
            {
                // Always clear the list at the end
                Clear();
            }
        }


        /// <summary>
        /// Dump the report lines to report.txt
        /// </summary>
        internal void Unload()
        {
            try
            {
                Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reporter.Unload Exception: {ex.Message}");
                // Fail silently or log, depending on your logging setup
            }
            finally
            {
                // Always clear the list at the end
                Clear();
            }
        }


        internal void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
        {
            this.dispatcherQueue = dispatcherQueue;
        }

        internal ListView GetListView() => ListViewReporter;

        internal void SetTextBoxStatus(TextBox textBoxStatus) => this.textBoxStatus = textBoxStatus;
        internal void SetTextBoxWarning(TextBox textBoxWarning) => this.textBoxWarning = textBoxWarning;
        internal void SetTextBoxError(TextBox textBoxError) => this.textBoxError = textBoxError;

        public void Clear()
        {
            ReportItems.Clear();

            // Don't reset the ItemSource as it means the reported is broken is a prior survey is loaded and closed
            //???ListViewReporter.ItemsSource = null;

            if (textBoxWarning is not null)
                textBoxWarning.Text = "";
            if (textBoxError is not null)
                textBoxError.Text = "";

            warningCount = 0;
            errorCount = 0;

            SetDirty(false);
        }

        public void Out(WarningLevel warningLevel, string channel, string message)
        {
            if (ListViewReporter != null && dispatcherQueue != null)
            {
                if (dispatcherQueue.HasThreadAccess)
                {
                    AddReportItem(warningLevel, channel, message);
                }
                else
                {
                    dispatcherQueue.TryEnqueue(() => AddReportItem(warningLevel, channel, message));
                }
                System.Diagnostics.Debug.WriteLine($"Reporter: {warningLevel} {channel} {message}");

                bDirty = true;
            }
        }

        private void AddReportItem(WarningLevel warningLevel, string channel, string message)
        {
            BitmapImage? imageData = warningLevel switch
            {
                WarningLevel.Info => imageDataInfo,
                WarningLevel.Warning => imageDataWarning,
                WarningLevel.Error => imageDataError,
                WarningLevel.Debug => imageDataDebug,
                _ => null
            };

            bool bAddToListView = warningLevel != WarningLevel.None && warningLevel != WarningLevel.Status || !string.IsNullOrEmpty(message);

            if (bAddToListView)
            {
                if (ReportItems.Count >= maxListViewItems && maxListViewItems > 0)
                {
                    ReportItems.RemoveAt(0);
                }

                if (itemLastStatus != null)
                {
                    ReportItems.Remove(itemLastStatus);
                }

                var reportItem = new ReporterListViewItem
                {
                    WarningLevel = warningLevel,
                    ImageData = imageData,
                    Time = DateTime.Now.ToString("dd MMM yy HH:mm:ss"),
                    Channel = channel,
                    Message = message
                };

                ReportItems.Add(reportItem);

                if (warningLevel == WarningLevel.Status)
                {
                    itemLastStatus = reportItem;
                }
                else
                {
                    itemLastStatus = null;
                }
            }

            if (textBoxStatus is not null)
                textBoxStatus.Text = message;

            if (warningLevel == WarningLevel.Warning)
            {
                warningCount++;
                if (textBoxWarning is not null)
                    textBoxWarning.Text = warningCount.ToString();
            }

            if (warningLevel == WarningLevel.Error)
            {
                errorCount++;
                if (textBoxError is not null)
                    textBoxError.Text = errorCount.ToString();
            }
        }

        public void Error(string channel, string message) => Out(WarningLevel.Error, channel, message);
        public void Warning(string channel, string message) => Out(WarningLevel.Warning, channel, message);
        public void Info(string channel, string message) => Out(WarningLevel.Info, channel, message);
        public void Debug(string channel, string message) => Out(WarningLevel.Debug, channel, message);

        public async void Display(ReporterListViewItem item)
        {
            var display = $"Level: {item.WarningLevel}\r\nReport Time: {item.Time}\r\n\r\n{item.Message}";

            var dataPackage = new DataPackage();
            dataPackage.SetText(item.Message);
            
            var messageDialog = new ContentDialog
            {
                Title = "Report Line",
                Content = display,
                SecondaryButtonText = "Copy",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,

                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                XamlRoot = this.Content.XamlRoot
            };

            var result = await messageDialog.ShowAsync();

            if (result == ContentDialogResult.Secondary)
                Clipboard.SetContent(dataPackage);
        }

        public bool IsDirty() => bDirty;
        public void SetDirty(bool bDirty) => this.bDirty = bDirty;
        public int GetWarningCount() => warningCount;
        public int GetErrorCount() => errorCount;

        private void ViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ListViewReporter.SelectedItem is ReporterListViewItem selectedItem)
            {
                Display(selectedItem);
            }
        }
    }

    public class WarningLevelToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                Reporter.WarningLevel.Error => new BitmapImage(new Uri("ms-appx:///Assets/rpterr.png")),
                Reporter.WarningLevel.Warning => new BitmapImage(new Uri("ms-appx:///Assets/rptwarn.ico")),
                Reporter.WarningLevel.Info => new BitmapImage(new Uri("ms-appx:///Assets/rptinfo.ico")),
                Reporter.WarningLevel.Debug => new BitmapImage(new Uri("ms-appx:///Assets/rptdbg.ico")),
                _ => new BitmapImage()
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
