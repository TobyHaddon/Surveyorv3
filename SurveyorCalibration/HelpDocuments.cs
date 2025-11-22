// Used to find doucments (PDF, MP4, DOC, XLS) in a "Help Documents" folder
// and populate a menu with them.
//
// Version 1.0 01 May 2025
// Version 1.1 22 Nov 2025

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinUIEx;

namespace Surveyor;

public sealed class HelpDocuments
{
    private record HelpDocumentItem(string FileSpec, string MenuText);

    private readonly List<HelpDocumentItem> pdfList = [];
    private readonly List<HelpDocumentItem> videoList = [];
    private readonly List<HelpDocumentItem> docList = [];
    private readonly List<HelpDocumentItem> xlsList = [];

    public async Task Initialize(IList<MenuFlyoutItemBase> helpMenuItems,
                           MenuFlyoutSeparator pdfSection,
                           MenuFlyoutSeparator videoSection,
                           MenuFlyoutSeparator docSection,
                           MenuFlyoutSeparator xlsSection)
    {
        Load();
        await Populate(helpMenuItems, pdfList, pdfSection, "\uE897"); // PDF icon
        await Populate(helpMenuItems, videoList, videoSection, "\uE7BE"); // MP4 icon
        await Populate(helpMenuItems, docList, docSection, "\uE8A5"); // DOC icon
        await Populate(helpMenuItems, xlsList, xlsSection, "\uE80A"); // XLS icon (grid)
    }

    private void Load()
    {
        var helpFolder = Path.Combine(AppContext.BaseDirectory, "Help Documents");
        if (!Directory.Exists(helpFolder)) return;

        foreach (var file in Directory.EnumerateFiles(helpFolder))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var fileName = Path.GetFileNameWithoutExtension(file);
            var cleanedName = Regex.Replace(fileName, @"^\d+[:.\s-]*", "");
            var item = new HelpDocumentItem(file, cleanedName);

            switch (ext)
            {
                case ".pdf":
                    pdfList.Add(item);
                    break;
                case ".mp4":
                    videoList.Add(item);
                    break;
                case ".doc":
                case ".docx":
                    docList.Add(item);
                    break;
                case ".xls":
                case ".xlsx":
                case ".xlsm":
                    xlsList.Add(item);
                    break;
            }
        }

        static void SortList(List<HelpDocumentItem> list) =>
            list.Sort((a, b) => string.Compare(a.FileSpec, b.FileSpec, StringComparison.OrdinalIgnoreCase));

        SortList(pdfList);
        SortList(videoList);
        SortList(docList);
        SortList(xlsList);
    }

    private async Task Populate(IList<MenuFlyoutItemBase> menuItems,
                          List<HelpDocumentItem> list,
                          MenuFlyoutSeparator section,
                          string iconUnicode)
    {
        int index = menuItems.IndexOf(section);
        section.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var doc in list)
        {
            var item = new MenuFlyoutItem
            {
                Text = doc.MenuText,
                Tag = doc.FileSpec,
            };

            // Try to get the application icon
            BitmapImage? appIcon = await VideoThumbnailHelper.GetFileThumbnailAsync(doc.FileSpec);
            if (appIcon != null)
            {
                item.Icon = appIcon;
            }
            else
            {
                item.Icon = new FontIcon
                {
                    Glyph = iconUnicode,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets")
                };
            }

            item.Click += OnHelpDocumentClick;
            menuItems.Insert(++index, item);
        }
    }

    private void OnHelpDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem clicked) return;
        var path = clicked.Tag as string;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open document: {ex.Message}");
            }
        }
    }
}
