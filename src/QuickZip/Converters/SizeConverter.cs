using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickZip.Converters
{
    /// <summary>
    /// 字节数转可读大小
    /// </summary>
    public class SizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long bytes = 0;
            if (value is long l) bytes = l;
            else if (value is int i) bytes = i;

            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
