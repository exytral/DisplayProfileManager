using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.UI.Icons;

namespace DisplayProfileManager.UI.Converters
{
    [ValueConversion(typeof(string), typeof(ImageSource))]
    public class ProfileIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int size = parameter != null && int.TryParse(parameter.ToString(), out int requestedSize) ? requestedSize : 24;

            return ProfileIconProvider.LoadImageSource(value as string, size);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    [ValueConversion(typeof(Profile), typeof(ImageSource))]
    public class SavedLayoutThumbnailConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int size = parameter != null && int.TryParse(parameter.ToString(), out int requestedSize) ? requestedSize : 24;

            return value is Profile profile ? SavedLayoutThumbnailRenderer.Create(profile.DisplaySettings, size) : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
