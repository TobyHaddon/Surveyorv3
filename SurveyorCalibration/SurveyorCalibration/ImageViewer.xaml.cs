using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.Controls
{
    public sealed partial class ImageStripViewer : UserControl
    {
        public ImageStripViewer()
        {
            this.InitializeComponent();
        }

        private string _data = string.Empty;

        public string Data
        {
            get => _data;
            set
            {
                _data = value;
                LoadImages(_data);
            }
        }

        private void LoadImages(string searchPattern)
        {
            try
            {
                string folder = Path.GetDirectoryName(searchPattern) ?? "";
                string pattern = Path.GetFileName(searchPattern);

                if (!Directory.Exists(folder))
                {
                    ImageList.ItemsSource = Array.Empty<BitmapImage>();
                    return;
                }

                var imagePaths = Directory.EnumerateFiles(folder, pattern)
                                            .OrderBy(p => p)
                                            .ToList();

                var images = new List<BitmapImage>();
                foreach (var path in imagePaths)
                {
                    var bitmap = new BitmapImage(new Uri(path));
                    images.Add(bitmap);
                }

                ImageList.ItemsSource = images;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ImageStripViewer error: {ex.Message}");
                ImageList.ItemsSource = Array.Empty<BitmapImage>();
            }
        }
    }
}

