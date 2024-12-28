using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyInfoAndMedia : UserControl
    {
        public SurveyInfoAndMedia()
        {
            this.InitializeComponent();
        }

        private void SurveyDepth_BeforeTextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Only allow numbers and a single decimal point
            sender.Text = Regex.Replace(sender.Text, @"[^0-9.]", "");

            // Prevent more than one decimal point
            if (Regex.IsMatch(sender.Text, @"\.\d*\.+"))
            {
                sender.Text = sender.Text.Remove(sender.Text.LastIndexOf('.'));
            }
        }

        private void MoveItemUp_Click(object sender, RoutedEventArgs e)
        {

        }

        private void MoveItemDown_Click(object sender, RoutedEventArgs e)
        {

        }

        private void MoveItemAcrossLeft_Click(object sender, RoutedEventArgs e)
        {

        }

        private void MoveItemAcrossRight_Click(object sender, RoutedEventArgs e)
        {

        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {

        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
