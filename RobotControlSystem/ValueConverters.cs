using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RobotControlSystem
{
    public class MessageTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string messageType)
            {
                return messageType switch
                {
                    "deviceStatus" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    "fireMainStatus" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    "analogData" => new SolidColorBrush(Color.FromRgb(156, 39, 176)),
                    "operation" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    "heartbeat" => new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                    _ => new SolidColorBrush(Color.FromRgb(96, 125, 139))
                };
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int statusCode)
            {
                return statusCode switch
                {
                    0 => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                    1 => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    2 => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))
                };
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Brushes.Green : Brushes.Red;
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
