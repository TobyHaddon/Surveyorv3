using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    public sealed partial class ExportUserControl : UserControl
    {
        public ExportUserControl()
        {
            InitializeComponent();
        }


        ///
        /// Events
        /// 

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileExport_Click(object sender, RoutedEventArgs e) => _ = FileExportAsync();
        private async Task FileExportAsync()
        {
            ExportContentDialog.XamlRoot = this.Content.XamlRoot;
            await ExportContentDialog.ShowAsync();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ExportSamplesNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            switch (tag)
            {
                case "cs":
                    ExportSamplesFrame.Content = new TextBlock { Text = "C# sample goes here", TextWrapping = TextWrapping.Wrap };
                    break;
                case "cpp":
                    ExportSamplesFrame.Content = new TextBlock { Text = "C++ sample goes here", TextWrapping = TextWrapping.Wrap };
                    break;
                case "py":
                    ExportSamplesFrame.Content = new TextBlock { Text = "Python sample goes here", TextWrapping = TextWrapping.Wrap };
                    break;
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ExportDialog_Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) => _ = ExportDialogSaveAsync(args);
        private async Task ExportDialogSaveAsync(ContentDialogButtonClickEventArgs args)
        {
            var picker = new FileSavePicker();
            var hWnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hWnd);

            // Suggested file types based on selected format
            if (FormatPdf.IsChecked == true)
            {
                picker.FileTypeChoices.Add("PDF", new[] { ".pdf" });
                picker.SuggestedFileName = "export.pdf";
            }
            else if (FormatOpenCV.IsChecked == true)
            {
                picker.FileTypeChoices.Add("OpenCV Data", new[] { ".yaml", ".yml", ".json" });
                picker.SuggestedFileName = "export.yml";
            }
            else
            {
                picker.FileTypeChoices.Add("Native", new[] { ".json" });
                picker.SuggestedFileName = "export.json";
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                args.Cancel = true; // keep dialog open if user cancels
                return;
            }

            // TODO: write export content to 'file'
            // await FileIO.WriteTextAsync(file, generatedContent);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ExportDialog_Cancel_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // no-op; dialog will close
        }

        // Optional: update preview when format changes
        private void FormatChanged(object sender, RoutedEventArgs e)
        {
            if (FormatPdf.IsChecked == true)
            {
                PreviewText.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;
                // PreviewImage.Source = ... // set image source to a PDF preview bitmap if available
            }
            else
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewText.Visibility = Visibility.Visible;
                PreviewText.Text = FormatOpenCV.IsChecked == true ? "OpenCV/YAML preview..." : "Native/JSON preview...";
            }
        }
    }
}
