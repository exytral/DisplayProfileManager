using DisplayProfileManager.Helpers;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DisplayProfileManager.UI.Converters
{
    [ValueConversion(typeof(string), typeof(ImageSource))]
    public class ProfileIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IconHelper.LoadImageSource(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}