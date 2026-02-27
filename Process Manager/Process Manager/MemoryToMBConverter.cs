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
                double mb = bytes / 1024.0 / 1024.0;
                return Math.Round(mb, 1); // 1 знак после запятой
            }
            return value ?? 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}