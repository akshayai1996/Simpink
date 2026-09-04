using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SimpinkNative.Controls
{
    public partial class ColorPicker : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
                new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public event EventHandler<ColorChangedEventArgs>? SelectedColorChanged;

        public ColorPicker()
        {
            InitializeComponent();
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPicker cp && e.NewValue is Color c)
            {
                cp.SelectedColorChanged?.Invoke(cp, new ColorChangedEventArgs(c));
            }
        }

        private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (ColorConverter.ConvertFromString(tb.Text) is Color c)
                {
                    SelectedColor = c;
                }
                else
                {
                    tb.Text = SelectedColor.ToString();
                }
            }
        }
    }

    public class ColorChangedEventArgs : EventArgs
    {
        public Color NewColor { get; }
        public ColorChangedEventArgs(Color color) => NewColor = color;
    }

    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Color c ? new SolidColorBrush(c) : Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is SolidColorBrush b ? b.Color : Colors.Transparent;
        }
    }

    public class ColorToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "#000000";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && ColorConverter.ConvertFromString(s) is Color c)
                return c;
            return Colors.Transparent;
        }
    }
}