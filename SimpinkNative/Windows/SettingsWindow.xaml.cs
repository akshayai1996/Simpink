using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SimpinkNative.Controls;
using SimpinkNative.Interop;
using SimpinkNative.Models;
using SimpinkNative.Services;

namespace SimpinkNative.Windows
{
    public partial class SettingsWindow : Window
    {
        private Settings _settings;
        private ToolbarWindow? _toolbar;
        public Settings ResultSettings { get; private set; } = null!;

        public SettingsWindow(Settings settings, ToolbarWindow toolbar)
        {
            InitializeComponent();
            _settings = settings;
            _toolbar = toolbar;
            LoadSettings();
        }

        private void LoadSettings()
        {
            SavePathDisplay.Text = string.IsNullOrEmpty(_settings.SavePath) 
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) 
                : _settings.SavePath;

            FontFamilyCombo.SelectedItem = FontFamilyCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == _settings.Text.FontFamily) ?? FontFamilyCombo.Items[0];
            TextColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(_settings.Text.Color);
            FontSizeSlider.Value = _settings.Text.Size;
            BoldToggle.IsChecked = _settings.Text.Bold;
            ItalicToggle.IsChecked = _settings.Text.Italic;

            VideoQualityCombo.SelectedItem = VideoQualityCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == _settings.VideoQuality) ?? VideoQualityCombo.Items[0];

            BuildPenSettings();
        }

        private void BuildPenSettings()
        {
            PenSettingsList.Items.Clear();
            for (int i = 0; i < _settings.Pens.Count; i++)
            {
                var pen = _settings.Pens[i];
                var border = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#253248")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var header = new TextBlock
                {
                    Text = $"Pen {i + 1}",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 50
                };
                header.SetValue(Grid.ColumnProperty, 0);
                grid.Children.Add(header);

                var colorPicker = new ColorPicker
                {
                    SelectedColor = (Color)ColorConverter.ConvertFromString(pen.Color),
                    Height = 36,
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                colorPicker.SelectedColorChanged += (_, e) => pen.Color = e.NewColor.ToString();
                colorPicker.SetValue(Grid.ColumnProperty, 1);
                grid.Children.Add(colorPicker);

                var alphaStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                var alphaSlider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = pen.Alpha,
                    Width = 100,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };
                alphaSlider.ValueChanged += (_, e) => pen.Alpha = (int)Math.Round(e.NewValue);
                alphaStack.Children.Add(alphaSlider);

                alphaStack.SetValue(Grid.ColumnProperty, 2);
                grid.Children.Add(alphaStack);

                border.Child = grid;
                PenSettingsList.Items.Add(border);
            }
        }

        private void PickFolder_Click(object sender, MouseButtonEventArgs e)
        {
            var path = NativeDialogs.BrowseForFolder(this, "Select Screenshot Folder", _settings.SavePath);
            if (path != null)
            {
                _settings.SavePath = path;
                SavePathDisplay.Text = path;
            }
        }

        private void BoldToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.Text.Bold = BoldToggle.IsChecked == true;
        }

        private void ItalicToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.Text.Italic = ItalicToggle.IsChecked == true;
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset all settings to defaults? This will erase custom pens, fonts, and save path.",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _settings = new Settings();
                LoadSettings();
            }
        }

        private void SaveClose_Click(object sender, RoutedEventArgs e)
        {
            _settings.Text.FontFamily = ((ComboBoxItem)FontFamilyCombo.SelectedItem)?.Tag?.ToString() ?? "Segoe UI";
            _settings.Text.Color = TextColorPicker.SelectedColor.ToString();
            _settings.Text.Size = FontSizeSlider.Value;
            _settings.VideoQuality = ((ComboBoxItem)VideoQualityCombo.SelectedItem)?.Tag?.ToString() ?? "hd";

            ResultSettings = _settings;
            DialogResult = true;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(hwnd);
            Left = _toolbar!.Left + (_toolbar.ActualWidth - ActualWidth) / 2;
            Top = _toolbar.Top + (_toolbar.ActualHeight - ActualHeight) / 2;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
