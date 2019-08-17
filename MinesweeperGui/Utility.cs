using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinesweeperGui
{
    /*
    *   This class holds some utility functions
    */
    public static class Utility {

        public static ImageSource BuildImageSource(string filename, int tileSize) {
            return BuildImageSource(filename, tileSize, tileSize);
        }

        public static ImageSource BuildImageSource(string filename, int tileWidth, int tileHeight) {

            Uri uri = new Uri("pack://application:,,,/resources/images/" + filename);

            // Create source.
            BitmapImage bi = new BitmapImage();
            // BitmapImage.UriSource must be in a BeginInit/EndInit block.
            bi.BeginInit();
            bi.UriSource = uri;
            bi.DecodePixelHeight = tileHeight;
            bi.DecodePixelWidth = tileWidth;
            bi.EndInit();

            bi.Freeze();

            return bi;

        }




        public static void Write(string text) {
            //System.Diagnostics.Debug.Print(text);
            Console.WriteLine(text);
        }

    }

}
