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
using Windows.Foundation;
using Windows.Foundation.Collections;


namespace Surveyor.User_Controls
{
    public sealed partial class Distortion3DView : UserControl
    {
        public Distortion3DView()
        {
            InitializeComponent();
        }


        ///
        /// EVENTS
        /// 
        
        private void ShowSurface_Checked(object sender, RoutedEventArgs e)
        {
           // Dist3D.ShowSurface = true;
        }

        private void ShowSurface_Unchecked(object sender, RoutedEventArgs e)
        {
         //   Dist3D.ShowSurface = false;
        }

        private void ShowWire_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void ShowWire_Unchecked(object sender, RoutedEventArgs e)
        {

        }
    }
}
