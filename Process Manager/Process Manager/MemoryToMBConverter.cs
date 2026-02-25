using System;
using System.Globalization;
using System.Windows.Data;

namespace ProcessManager.Converters
{
    public class MemoryToMBConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                // Делим байты на 1024² = 1 МБ
                double megabytes = bytes / 1024.0 / 1024.0;
                return Math.Round(megabytes, 1); // 1 знак после запятой
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}