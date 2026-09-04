using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SimpinkNative.Interop;
using SimpinkNative.Models;
using SimpinkNative.Services;

namespace SimpinkNative.Windows
{
    public partial class ToolbarWindow : Window
    {
        private OverlayWindow? _overlay;
        private bool _dragging;
        private Point _dragStart;
        private Point _windowStart;
        private bool _wasDragged;
        private bool _minimized;
        private bool _isCanvasMode;
        private Settings _settings = new();
        private Button? _activeToolButton;
        private Button? _pointerButton;
        private Button? _recordButton;
        private Button? _stopButton;
        private Button? _pauseButton;
        private Slider? _widthSlider;
        private StackPanel? _penGrid;
        private int _activePenIndex;
        private readonly Dictionary<Button, ToolType> _toolButtons = new();

        public ToolbarWindow()
        {
            InitializeComponent();
            BuildToolbar();
        }

        public void SetOverlay(OverlayWindow overlay) => _overlay = overlay;

        private void BuildToolbar()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ToolBarPanel.Children.Add(panel);

            // 1. HANDLE / MINIMIZE
            var handleBtn = CreateIconButton(CreateChevronLeft(), "Minimize", () => ToggleMinimize());
            handleBtn.Width = 24;
            panel.Children.Add(handleBtn);

            panel.Children.Add(CreateDivider());

            // 2. PEN GRID (2 rows x 3 swatches)
            _penGrid = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(_penGrid);

            panel.Children.Add(CreateDivider());

            // 3. STROKE WIDTH SLIDER
            var sliderBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(8, 0, 0, 0)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center
            };
            var sliderStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lineIcon = CreatePathIcon("M12 20V4", 12, 12, strokeWidth: 3);
            sliderStack.Children.Add(lineIcon);

            _widthSlider = new Slider
            {
                Style = FindResource("SimpinkSlider") as Style,
                Minimum = 1,
                Maximum = 15,
                Value = 3,
                Width = 60,
                Height = 18,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _widthSlider.ValueChanged += (_, e) => _overlay?.SetPenWidth(e.NewValue);
            sliderStack.Children.Add(_widthSlider);
            sliderBox.Child = sliderStack;
            panel.Children.Add(sliderBox);

            panel.Children.Add(CreateDivider());

            // 4. DRAWING TOOLS GROUP
            var penBtn = CreateToolButton(CreatePenIcon(), "Brush", ToolType.Pen);
            _toolButtons[penBtn] = ToolType.Pen;
            panel.Children.Add(penBtn);

            var eraserBtn = CreateToolButton(CreateEraserIcon(), "Eraser", ToolType.Eraser);
            _toolButtons[eraserBtn] = ToolType.Eraser;
            panel.Children.Add(eraserBtn);

            var textBtn = CreateToolButton(CreateTextIcon(), "Text (Right-click for Font)", ToolType.Text);
            _toolButtons[textBtn] = ToolType.Text;
            textBtn.ContextMenu = new ContextMenu();
            var miText = new MenuItem { Header = "Font Settings..." };
            miText.Click += (_, _) => ShowSettings();
            textBtn.ContextMenu.Items.Add(miText);
            panel.Children.Add(textBtn);

            var lineBtn = CreateToolButton(CreateLineIcon(), "Line", ToolType.Line);
            _toolButtons[lineBtn] = ToolType.Line;
            panel.Children.Add(lineBtn);

            var rectBtn = CreateToolButton(CreateRectIcon(), "Rectangle", ToolType.Rect);
            _toolButtons[rectBtn] = ToolType.Rect;
            panel.Children.Add(rectBtn);

            var circleBtn = CreateToolButton(CreateCircleIcon(), "Ellipse", ToolType.Circle);
            _toolButtons[circleBtn] = ToolType.Circle;
            panel.Children.Add(circleBtn);

            panel.Children.Add(CreateDivider());

            // 5. ARROWS GROUP
            var arrowBtn = CreateToolButton(CreateArrowIcon(), "Single Arrow", ToolType.Arrow);
            _toolButtons[arrowBtn] = ToolType.Arrow;
            panel.Children.Add(arrowBtn);

            var dArrowBtn = CreateToolButton(CreateDoubleArrowIcon(), "Double Arrow", ToolType.DoubleArrow);
            _toolButtons[dArrowBtn] = ToolType.DoubleArrow;
            panel.Children.Add(dArrowBtn);

            panel.Children.Add(CreateDivider());

            // 5.5 UNDO / REDO GROUP
            var undoBtn = CreateIconButton(CreateUndoIcon(), "Undo (Ctrl+Z)", () => _overlay?.Undo());
            panel.Children.Add(undoBtn);

            var redoBtn = CreateIconButton(CreateRedoIcon(), "Redo (Ctrl+Y)", () => _overlay?.Redo());
            panel.Children.Add(redoBtn);

            panel.Children.Add(CreateDivider());

            // 6. MOUSE & EDIT GROUP
            _pointerButton = CreateToolButton(CreatePointerIcon(), "Highlighter Mouse", ToolType.Pen, isPointer: true);
            panel.Children.Add(_pointerButton);

            var moveBtn = CreateToolButton(CreateMoveIcon(), "Move", ToolType.Move);
            _toolButtons[moveBtn] = ToolType.Move;
            panel.Children.Add(moveBtn);

            var copyBtn = CreateIconButton(CreateCopyIcon(), "Copy", () => _overlay?.CloneLast());
            panel.Children.Add(copyBtn);

            panel.Children.Add(CreateDivider());

            // 7. BACKGROUND MODES GROUP
            var bgNoneBtn = CreateBackgroundButton("Transparent", CreateCheckerIcon(), BackgroundMode.None);
            panel.Children.Add(bgNoneBtn);

            var bgWhiteBtn = CreateBackgroundButton("White", CreateSolidIcon(Colors.White), BackgroundMode.White);
            panel.Children.Add(bgWhiteBtn);

            var bgDarkBtn = CreateBackgroundButton("Black", CreateSolidIcon(Color.FromRgb(0x1E, 0x29, 0x3B)), BackgroundMode.Dark);
            panel.Children.Add(bgDarkBtn);

            var bgBlurBtn = CreateBackgroundButton("Blur", CreateBlurIcon(), BackgroundMode.Blur);
            panel.Children.Add(bgBlurBtn);

            panel.Children.Add(CreateDivider());

            // 8. ACTIONS GROUP
            var snapBtn = CreateIconButton(CreateSnapIcon(), "Snipping tool (Right-click to set path)", () => _overlay?.StartSnap());
            snapBtn.ContextMenu = new ContextMenu();
            var miPath = new MenuItem { Header = "Set Save Folder..." };
            miPath.Click += (_, _) => PickFolder();
            snapBtn.ContextMenu.Items.Add(miPath);
            panel.Children.Add(snapBtn);

            _recordButton = CreateIconButton(CreateRecordIcon(), "Recording (Right-click for Quality)");
            _recordButton.Click += (_, _) => _overlay?.ToggleRecording();
            _recordButton.ContextMenu = new ContextMenu();
            foreach (var q in new[] { ("High Definition (10 Mbps)", "hd"), ("Pro-Grade (25 Mbps)", "pro"), ("Standard (5 Mbps)", "std") })
            {
                var qmi = new MenuItem { Header = q.Item1, Tag = q.Item2 };
                qmi.Click += (_, e) => { if (e.Source is MenuItem m) SetVideoQuality(m.Tag.ToString()!); };
                _recordButton.ContextMenu.Items.Add(qmi);
            }
            panel.Children.Add(_recordButton);

            _stopButton = CreateIconButton(CreateStopIcon(), "Stop Recording");
            _stopButton.Visibility = Visibility.Collapsed;
            _stopButton.Click += (_, _) => _overlay?.ToggleRecording();
            panel.Children.Add(_stopButton);

            _pauseButton = CreateIconButton(CreatePauseIcon(), "Pause/Resume");
            _pauseButton.Visibility = Visibility.Collapsed;
            _pauseButton.Click += (_, _) => _overlay?.PauseRecording();
            panel.Children.Add(_pauseButton);

            var resetBtn = CreateIconButton(CreateResetIcon(), "Reset to Defaults", () => _overlay?.ResetDefaults());
            panel.Children.Add(resetBtn);

            var clearBtn = CreateIconButton(CreateClearIcon(), "Clear All");
            clearBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            clearBtn.Click += (_, _) => _overlay?.ClearCanvas();
            panel.Children.Add(clearBtn);

            var exitBtn = CreateIconButton(CreateExitIcon(), "Exit", () => Application.Current.Shutdown());
            exitBtn.Style = FindResource("DangerButton") as Style;
            panel.Children.Add(exitBtn);

            // Default State
            UpdateToolButtonState(_pointerButton);
        }

        private Button CreateIconButton(UIElement icon, string tooltip, Action? action = null)
        {
            var btn = new Button { Style = FindResource("ToolbarButton") as Style, ToolTip = tooltip };
            btn.Content = icon;
            if (action != null) btn.Click += (_, _) => action();
            return btn;
        }

        private Button CreateToolButton(UIElement icon, string tooltip, ToolType tool, bool isPointer = false)
        {
            var btn = new Button { Style = FindResource("ToolbarButton") as Style, ToolTip = tooltip, Tag = tool };
            btn.Content = icon;
            btn.Click += ToolButton_Click;
            if (isPointer) _pointerButton = btn;
            return btn;
        }

        private Button CreateBackgroundButton(string tooltip, UIElement icon, BackgroundMode mode)
        {
            var btn = new Button { Style = FindResource("ToolbarButton") as Style, ToolTip = tooltip, Tag = mode };
            btn.Content = icon;
            btn.Width = 36;
            btn.Height = 36;
            btn.Click += BgButton_Click;
            return btn;
        }

        private Border CreateDivider()
        {
            return new Border
            {
                Width = 1,
                Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                if (btn == _pointerButton)
                {
                    _overlay?.SetPointerMode();
                    UpdateToolButtonState(_pointerButton);
                    return;
                }

                if (btn.Tag is ToolType tool)
                {
                    _overlay?.SetTool(tool);
                    UpdateToolButtonState(btn);
                }
            }
        }

        private void BgButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BackgroundMode mode)
            {
                _overlay?.SetBackground(mode);
            }
        }

        public void SetActiveTool(ToolType tool)
        {
            var btn = _toolButtons.FirstOrDefault(kvp => kvp.Value == tool).Key;
            if (btn != null) UpdateToolButtonState(btn);
        }

        public void SetPointerMode()
        {
            UpdateToolButtonState(_pointerButton);
        }

        private void UpdateToolButtonState(Button? activeBtn)
        {
            foreach (var kvp in _toolButtons)
            {
                kvp.Key.Style = kvp.Key == activeBtn ? FindResource("ToolbarButtonActive") as Style : FindResource("ToolbarButton") as Style;
            }
            if (_pointerButton != null)
            {
                _pointerButton.Style = _pointerButton == activeBtn ? FindResource("ToolbarButtonActive") as Style : FindResource("ToolbarButton") as Style;
            }
            _activeToolButton = activeBtn;
        }

        public void SetRecordingState(bool recording, bool paused)
        {
            if (_recordButton == null || _pauseButton == null || _stopButton == null) return;

            if (recording)
            {
                _recordButton.Visibility = Visibility.Collapsed;
                _stopButton.Visibility = Visibility.Visible;
                _pauseButton.Visibility = Visibility.Visible;
                _pauseButton.Style = paused ? FindResource("ToolbarButtonActive") as Style : FindResource("ToolbarButton") as Style;
                MainBar.Effect = new DropShadowEffect { Color = Colors.Red, ShadowDepth = 0, BlurRadius = 20, Opacity = 0.5 };
            }
            else
            {
                _recordButton.Visibility = Visibility.Visible;
                _stopButton.Visibility = Visibility.Collapsed;
                _pauseButton.Visibility = Visibility.Collapsed;
                MainBar.Effect = Resources["GlassEffect"] as Effect;
            }
        }

        public void UpdatePenUI(List<PenConfig> pens, int activeIndex)
        {
            _activePenIndex = activeIndex;
            if (_penGrid == null) return;
            _penGrid.Children.Clear();

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal };

            for (int i = 0; i < pens.Count; i++)
            {
                var pen = pens[i];
                var color = (Color)ColorConverter.ConvertFromString(pen.Color);
                var fillBrush = new SolidColorBrush(color) { Opacity = pen.Alpha / 100.0 };
                bool isActive = i == activeIndex;

                var outerBorder = new Border
                {
                    Width = 19,
                    Height = 19,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2, 0, 2, 0),
                    Background = fillBrush,
                    BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5)) : Brushes.Transparent,
                    BorderThickness = isActive ? new Thickness(2) : new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"Pen {i + 1} (Right-click for settings)",
                    Tag = i
                };

                outerBorder.MouseLeftButtonDown += (s, _) =>
                {
                    if (s is Border b && b.Tag is int idx)
                    {
                        _overlay?.SetActivePen(idx);
                        UpdatePenUI(pens, idx);
                    }
                };

                outerBorder.ContextMenu = new ContextMenu();
                var mi = new MenuItem { Header = "Pen Settings..." };
                mi.Click += (_, _) => ShowSettings();
                outerBorder.ContextMenu.Items.Add(mi);

                if (i < 3) row1.Children.Add(outerBorder);
                else row2.Children.Add(outerBorder);
            }

            _penGrid.Children.Add(row1);
            _penGrid.Children.Add(row2);
        }

        public void UpdatePenWidth(double w) => _widthSlider!.Value = w;

        public void SetActivePenColor(string color, int alpha)
        {
            if (_penGrid != null)
            {
                int count = 0;
                foreach (StackPanel row in _penGrid.Children.OfType<StackPanel>())
                {
                    foreach (Border b in row.Children.OfType<Border>())
                    {
                        if (count == _activePenIndex)
                        {
                            var c = (Color)ColorConverter.ConvertFromString(color);
                            b.Background = new SolidColorBrush(c) { Opacity = alpha / 100.0 };
                            return;
                        }
                        count++;
                    }
                }
            }
        }

        public void UpdateTextConfig(TextConfig tc) { }

        public void UpdateVideoQuality(string vq) { }

        public void SetSavePath(string path) { }

        private void ToggleMinimize()
        {
            _minimized = !_minimized;
            OrbBorder.Visibility = _minimized ? Visibility.Visible : Visibility.Collapsed;
            MainBar.Visibility = _minimized ? Visibility.Collapsed : Visibility.Visible;
            if (!_minimized) ClampToScreen();
        }

        private void ContextMinimize_Click(object sender, RoutedEventArgs e) => ToggleMinimize();

        private void ContextExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void ClampToScreen()
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;

            double left = Math.Max(vLeft, Math.Min(Left, vLeft + vWidth - width));
            double top = Math.Max(vTop, Math.Min(Top, vTop + vHeight - height));
            Left = left;
            Top = top;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(hwnd);
            Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 40;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Border || e.Source is Grid || e.Source is StackPanel || e.Source is Image)
            {
                _dragging = true;
                _wasDragged = false;
                _dragStart = PointToScreen(e.GetPosition(this));
                _windowStart = new Point(Left, Top);
                CaptureMouse();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var current = PointToScreen(e.GetPosition(this));
            var offset = current - _dragStart;

            if (Math.Abs(offset.X) > 3 || Math.Abs(offset.Y) > 3)
                _wasDragged = true;

            Left = _windowStart.X + offset.X;
            Top = _windowStart.Y + offset.Y;
            ClampToScreen();
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                ClampToScreen();

                if (!_wasDragged && _minimized)
                {
                    ToggleMinimize();
                }
            }
        }

        private void Orb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Handled in Window_MouseLeftButtonDown
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                ClampToScreen();
            }
        }

        public void SetCanvasMode(bool isCanvasMode)
        {
            _isCanvasMode = isCanvasMode;
            Opacity = (_isCanvasMode && !IsMouseOver) ? 0.35 : 1.0;
            Topmost = false;
            Topmost = true;
        }

        public void UpdateProximity(Point mouseScreenPos)
        {
            if (!_isCanvasMode || _minimized) return;

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            var windowRect = new Rect(Left, Top, width, height);
            
            var proximityRect = windowRect;
            proximityRect.Inflate(140, 120);

            bool isNear = proximityRect.Contains(mouseScreenPos) || IsMouseOver;
            double targetOpacity = isNear ? 1.0 : 0.35;

            if (Math.Abs(Opacity - targetOpacity) > 0.01)
            {
                Opacity = targetOpacity;
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Opacity = 1.0;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Opacity = _isCanvasMode ? 0.35 : 1.0;
        }

        public void ShowSettings()
        {
            var dlg = new SettingsWindow(_settings, this);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _settings = dlg.ResultSettings;
                _overlay?.UpdatePenConfig(_settings.Pens);
                _overlay?.UpdateTextConfig(_settings.Text);
                _overlay?.UpdateVideoQuality(_settings.VideoQuality);
                _overlay?.SetSavePath(_settings.SavePath);
                UpdatePenUI(_settings.Pens, _settings.ActivePenIndex);
                UpdatePenWidth(_settings.PenWidth);
                SettingsStore.Save(_settings);
            }
        }

        private void PickFolder()
        {
            var path = NativeDialogs.BrowseForFolder(this, "Select Save Folder", _settings.SavePath);
            if (path != null)
            {
                _settings.SavePath = path;
                _overlay?.SetSavePath(_settings.SavePath);
                SettingsStore.Save(_settings);
            }
        }

        private void SetVideoQuality(string vq)
        {
            _settings.VideoQuality = vq;
            SettingsStore.Save(_settings);
        }

        #region Icon Creation Helpers
        private static UIElement CreatePathIcon(string pathData, double width = 18, double height = 18, bool fill = false, double strokeWidth = 2.2)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(pathData),
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeThickness = strokeWidth
            };

            path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });

            if (fill)
            {
                path.SetBinding(System.Windows.Shapes.Path.FillProperty, new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
                });
            }
            else
            {
                path.Fill = Brushes.Transparent;
            }

            return path;
        }

        private static UIElement CreateChevronLeft()
        {
            return CreatePathIcon("M15 18L9 12L15 6", 14, 14);
        }

        private static UIElement CreatePenIcon()
        {
            return CreatePathIcon("M12 19L19 12L22 15L15 22L12 19Z M18 13L16.5 5.5L2 2L3.5 14.5L13 18L18 13Z", 18, 18);
        }

        private static UIElement CreateEraserIcon()
        {
            return CreatePathIcon("M7 21L2.7 16.7C1.7 15.7 1.7 14.2 2.7 13.2L12.3 3.6C13.3 2.6 14.8 2.6 15.8 3.6L21.4 9.2C22.4 10.2 22.4 11.7 21.4 12.7L13 21 M22 21H7 M5 11L14 20", 18, 18);
        }

        private static UIElement CreateTextIcon()
        {
            return CreatePathIcon("M4 7V4H20V7 M12 4V20", 18, 18);
        }

        private static UIElement CreateLineIcon()
        {
            return CreatePathIcon("M4 20L20 4", 18, 18);
        }

        private static UIElement CreateRectIcon()
        {
            return CreatePathIcon("M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z", 18, 18);
        }

        private static UIElement CreateCircleIcon()
        {
            return CreatePathIcon("M12 3A9 9 0 1 0 12 21A9 9 0 1 0 12 3Z", 18, 18);
        }

        private static UIElement CreateArrowIcon()
        {
            return CreatePathIcon("M5 12H19 M12 5L19 12L12 19", 18, 18);
        }

        private static UIElement CreateDoubleArrowIcon()
        {
            return CreatePathIcon("M7 7L2 12L7 17 M17 17L22 12L17 7 M2 12H22", 18, 18);
        }

        private static UIElement CreateMoveIcon()
        {
            return CreatePathIcon("M5 9L2 12L5 15 M9 5L12 2L15 5 M15 19L12 22L9 19 M19 15L22 12L19 9 M2 12H22 M12 2V22", 18, 18);
        }

        private static UIElement CreateCopyIcon()
        {
            return CreatePathIcon("M11 9H20A2 2 0 0 1 22 11V20A2 2 0 0 1 20 22H11A2 2 0 0 1 9 20V11A2 2 0 0 1 11 9Z M5 15H4A2 2 0 0 1 2 13V4A2 2 0 0 1 4 2H13A2 2 0 0 1 15 4V5", 18, 18);
        }

        private static UIElement CreateUndoIcon()
        {
            return CreatePathIcon("M3 10H13A5 5 0 0 1 13 20H8 M8 5L3 10L8 15", 18, 18);
        }

        private static UIElement CreateRedoIcon()
        {
            return CreatePathIcon("M21 10H11A5 5 0 0 0 11 20H16 M16 5L21 10L16 15", 18, 18);
        }

        private static UIElement CreatePointerIcon()
        {
            var grid = new Grid { Width = 20, Height = 20 };
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new SolidColorBrush(Color.FromArgb(77, 251, 191, 36)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
                StrokeThickness = 1.2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var cursorPath = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M12 12L14.5 19.5L16 18L20 22L21 21L17 17L18.5 15.5Z"),
                Fill = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                StrokeThickness = 1
            };
            grid.Children.Add(circle);
            grid.Children.Add(cursorPath);
            return grid;
        }

        private static UIElement CreateSnapIcon()
        {
            return CreatePathIcon("M23 19A2 2 0 0 1 21 21H3A2 2 0 0 1 1 19V8A2 2 0 0 1 3 6H7L9 3H15L17 6H21A2 2 0 0 1 23 8Z M12 9A4 4 0 1 0 12 17A4 4 0 1 0 12 9Z", 18, 18);
        }

        private static UIElement CreateRecordIcon()
        {
            var grid = new Grid { Width = 20, Height = 20 };
            var outerCircle = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            outerCircle.SetBinding(System.Windows.Shapes.Ellipse.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            var innerDot = new System.Windows.Shapes.Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(outerCircle);
            grid.Children.Add(innerDot);
            return grid;
        }

        private static UIElement CreateStopIcon()
        {
            var grid = new Grid { Width = 20, Height = 20 };
            var outerCircle = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            outerCircle.SetBinding(System.Windows.Shapes.Ellipse.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            var innerSquare = new System.Windows.Shapes.Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(outerCircle);
            grid.Children.Add(innerSquare);
            return grid;
        }

        private static UIElement CreatePauseIcon()
        {
            return CreatePathIcon("M6 4H10V20H6Z M14 4H18V20H14Z", 18, 18, fill: true);
        }

        private static UIElement CreateResetIcon()
        {
            return CreatePathIcon("M3 12A9 9 0 1 0 12 3A9.75 9.75 0 0 0 5.26 5.74L3 8 M3 3V8H8", 18, 18);
        }

        private static UIElement CreateClearIcon()
        {
            return CreatePathIcon("M3 6H21 M19 6V20A2 2 0 0 1 17 22H7A2 2 0 0 1 5 20V6 M8 6V4A2 2 0 0 1 10 2H14A2 2 0 0 1 16 4V6", 18, 18);
        }

        private static UIElement CreateExitIcon()
        {
            return CreatePathIcon("M18 6L6 18 M6 6L18 18", 18, 18);
        }

        private static UIElement CreateCheckerIcon()
        {
            var db = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 10, 10),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing(Brushes.LightGray, null, Geometry.Parse("M0 0H10V10H0V0 M10 10H20V20H10V10"))
            };
            return new Border { Width = 18, Height = 18, Background = db, CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), BorderThickness = new Thickness(1) };
        }

        private static UIElement CreateSolidIcon(Color c)
        {
            return new Border { Width = 18, Height = 18, Background = new SolidColorBrush(c), CornerRadius = new CornerRadius(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), BorderThickness = new Thickness(1) };
        }

        private static UIElement CreateBlurIcon()
        {
            var grid = new Grid { Width = 20, Height = 20 };
            var outerCircle = new System.Windows.Shapes.Ellipse
            {
                Width = 18,
                Height = 18,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            outerCircle.SetBinding(System.Windows.Shapes.Ellipse.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            var innerDot = new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            innerDot.SetBinding(System.Windows.Shapes.Ellipse.FillProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            grid.Children.Add(outerCircle);
            grid.Children.Add(innerDot);
            return grid;
        }
        #endregion
    }
}