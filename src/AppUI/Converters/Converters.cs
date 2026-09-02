using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AppUI.Models;

namespace AppUI.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
        public Brush FalseBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return TrueBrush;
            }
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToStatusTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "Active";
        public string FalseText { get; set; } = "Inactive";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return TrueText;
            }
            return FalseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class FrameTimePointsConverter : IValueConverter
    {
        public double GraphWidth { get; set; } = 600.0;
        public double GraphHeight { get; set; } = 160.0;
        public double MaxFrameTimeMs { get; set; } = 33.33; // 30 FPS floor reference

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var points = new PointCollection();

            if (value is not ObservableCollection<float> history || history.Count == 0)
            {
                return points;
            }

            int count = history.Count;
            double stepX = GraphWidth / Math.Max(1, count - 1);

            for (int i = 0; i < count; i++)
            {
                float ft = history[i];
                double clampedFt = Math.Clamp(ft, 0.0, MaxFrameTimeMs);
                // Invert Y coordinate so 0ms is at the bottom and MaxFrameTimeMs is at top
                double y = GraphHeight - (clampedFt / MaxFrameTimeMs * GraphHeight);
                double x = i * stepX;

                points.Add(new Point(x, y));
            }

            return points;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string format && value != null)
            {
                return string.Format(culture, format, value);
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                return v != Visibility.Visible;
            }
            return false;
        }
    }
}
