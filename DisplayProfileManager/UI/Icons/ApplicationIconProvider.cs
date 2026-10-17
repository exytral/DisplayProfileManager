using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.UI.Icons
{
    internal static class ApplicationIconProvider
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public static Icon LoadIcon()
        {
            try
            {
                string path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load application icon from executable");
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        public static ImageSource LoadImageSource(int size = 16)
        {
            try
            {
                using Icon icon = LoadIcon();
                using var sized = new Icon(icon, size, size);
                using var bitmap = sized.ToBitmap();
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to create application icon image source");
                return null;
            }
        }
    }
}
