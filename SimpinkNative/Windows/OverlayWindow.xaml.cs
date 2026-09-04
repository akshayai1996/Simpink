using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SimpinkNative.Interop;
using SimpinkNative.Models;
using SimpinkNative.Services;

namespace SimpinkNative.Windows
{
    public partial class OverlayWindow : Window
    {
        private readonly List<DrawItem> _items = new();
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private DrawItem? _currentItem;
        private bool _drawing;
        private bool _isPointerMode = true;
        private bool _isSnapMode;
        private ToolType _activeTool = ToolType.Pen;
        private int _activePenIndex = 0;
        private double _penWidth = 3;
        private Point _startPoint;
        private Point _lastPoint;
        private Point _mousePos;
        private bool _shiftPressed;
        private Rect _snapStartRect;
        private bool _snapDrawing;
        private BackgroundMode _bgMode = BackgroundMode.None;
        private BitmapSource? _blurBackground;
        private TextBox? _textEditor;
        private int _selectedIndex = -1;
        private Thread? _recordThread;
        private CancellationTokenSource? _recordCts;
        private Recorder? _recorder;
        private bool _recording;
        private bool _recordingPaused;
        private Rect _recordingBounds;
        private Settings _settings = new();
        private readonly ToolbarWindow _toolbar;
        private readonly HotkeyManager _hotkeys;

        public OverlayWindow(ToolbarWindow toolbar)
        {
            _toolbar = toolbar;
            InitializeComponent();
            DrawCanvas.Owner = this;

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _hotkeys = new HotkeyManager(this);
            _hotkeys.HotkeyPressed += OnHotkey;

            // Initialize halo to the live cursor so it never first renders at (0,0)
            var cursor = Win32.GetCursorPosition();
            var dpiScale = ScreenCapture.GetDpiScale();
            _mousePos = new Point(cursor.X / dpiScale - Left, cursor.Y / dpiScale - Top);

            if (Environment.GetEnvironmentVariable("SIMPINK_REDPROBE") == "1")
            {
                _items.Add(new CircleItem { Start = new Point(640 - 60, 360 - 60), End = new Point(640 + 60, 360 + 60), Color = Colors.Red, Width = 8 });
            }

            LoadSettings();
            CompositionTarget.Rendering += OnRendering;
        }

        private void LoadSettings()
        {
            _settings = SettingsStore.Load();
            _activePenIndex = _settings.ActivePenIndex;
            _penWidth = _settings.PenWidth;
            ApplyPenSettings();
            _toolbar.UpdatePenUI(_settings.Pens, _activePenIndex);
            _toolbar.UpdatePenWidth(_penWidth);
            _toolbar.UpdateTextConfig(_settings.Text);
            _toolbar.UpdateVideoQuality(_settings.VideoQuality);
            _toolbar.SetSavePath(_settings.SavePath);
        }

        private void ApplyPenSettings()
        {
            if (_activePenIndex >= 0 && _activePenIndex < _settings.Pens.Count)
            {
                var pen = _settings.Pens[_activePenIndex];
                _toolbar.SetActivePenColor(pen.Color, pen.Alpha);
            }
        }

        private void SaveSettings()
        {
            _settings.ActivePenIndex = _activePenIndex;
            _settings.PenWidth = _penWidth;
            SettingsStore.Save(_settings);
        }

        private void SetupHaloTimer()
        {
        }

        private void UpdateHaloPosition()
        {
            // Always track the live cursor position so the halo is never stale,
            // regardless of the current tool/mode.
            var pt = Win32.GetCursorPosition();
            var dpiScale = ScreenCapture.GetDpiScale();
            _mousePos = new Point(pt.X / dpiScale - Left, pt.Y / dpiScale - Top);

            _toolbar.UpdateProximity(new Point(pt.X / dpiScale, pt.Y / dpiScale));

            if (_isPointerMode && !_isSnapMode)
                DrawCanvas.InvalidateVisual();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            UpdateHaloPosition();
            DrawCanvas.InvalidateVisual();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(hwnd);
            SetClickThrough(true);
            UpdateBackground();
            _hotkeys.RegisterHotkey(1, Win32.MOD_CONTROL | Win32.MOD_ALT, 0x52); // Ctrl+Alt+R
            _hotkeys.RegisterHotkey(2, Win32.MOD_CONTROL | Win32.MOD_ALT, 0x50); // Ctrl+Alt+P
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveSettings();
            _recordCts?.Cancel();
            _recorder?.Dispose();
            _hotkeys.Dispose();
            CompositionTarget.Rendering -= OnRendering;
        }

        public void SetClickThrough(bool enable)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetClickThrough(hwnd, enable);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift) _shiftPressed = true;
            if (e.Key == Key.Escape)
            {
                if (!_isPointerMode)
                {
                    SetPointerMode();
                }
                else
                {
                    ClearCanvas();
                }
            }
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Undo();
            if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Redo();
            if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) CloneLast();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift) _shiftPressed = false;
            base.OnKeyUp(e);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            if (_textEditor != null) return;
            var pos = e.GetPosition(DrawCanvas);

            if (_isSnapMode)
            {
                _startPoint = pos;
                _snapStartRect = new Rect(pos.X, pos.Y, 0, 0);
                _snapDrawing = true;
                _drawing = true;
                CaptureMouse();
                return;
            }

            if (_isPointerMode) return;

            if (_activeTool == ToolType.Eraser)
            {
                int idx = HitTest(pos);
                if (idx >= 0) { SaveState(); _items.RemoveAt(idx); InvalidateVisual(); }
                return;
            }

            if (_activeTool == ToolType.Move)
            {
                int hit = HitTest(pos);
                if (hit >= 0)
                {
                    SaveState();
                    _currentItem = _items[hit];
                    
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // Ctrl+Drag to clone
                        _currentItem = _currentItem.Clone();
                        _selectedIndex = _items.Count; // will be inserted at the end
                    }
                    else
                    {
                        _items.RemoveAt(hit);
                        _selectedIndex = hit;
                    }

                    _drawing = true;
                    _lastPoint = pos;
                    CaptureMouse();
                }
                else
                {
                    _selectedIndex = -1;
                }
                InvalidateVisual();
                return;
            }

            if (_activeTool == ToolType.Text)
            {
                CreateTextEditor(pos);
                return;
            }

            _startPoint = pos;
            _lastPoint = pos;
            _drawing = true;
            CaptureMouse();

            var pen = _settings.Pens[_activePenIndex];
            Color color = (Color)ColorConverter.ConvertFromString(pen.Color);
            double alpha = pen.Alpha / 100.0;

            _currentItem = _activeTool switch
            {
                ToolType.Pen => new PenStroke { Points = new List<Point> { pos }, Color = color, Alpha = alpha, Width = _penWidth },
                ToolType.Line => new LineItem { Start = pos, End = pos, Color = color, Alpha = alpha, Width = _penWidth },
                ToolType.Arrow => new ArrowItem { Start = pos, End = pos, Color = color, Alpha = alpha, Width = _penWidth },
                ToolType.DoubleArrow => new DoubleArrowItem { Start = pos, End = pos, Color = color, Alpha = alpha, Width = _penWidth },
                ToolType.Rect => new RectItem { Start = pos, End = pos, Color = color, Alpha = alpha, Width = _penWidth },
                ToolType.Circle => new CircleItem { Start = pos, End = pos, Color = color, Alpha = alpha, Width = _penWidth },
                _ => null
            };

            if (_currentItem != null) _currentItem.UpdateBounds();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var pos = e.GetPosition(DrawCanvas);
            _mousePos = pos;



            if (_isSnapMode && _snapDrawing)
            {
                _snapStartRect = new Rect(
                    Math.Min(_startPoint.X, pos.X),
                    Math.Min(_startPoint.Y, pos.Y),
                    Math.Abs(pos.X - _startPoint.X),
                    Math.Abs(pos.Y - _startPoint.Y));
                InvalidateVisual();
                return;
            }

            if (!_drawing || _currentItem == null) return;

            if (_activeTool == ToolType.Move && _currentItem != null)
            {
                var offset = pos - _lastPoint;
                _currentItem.Translate(offset);
                _lastPoint = pos;
                InvalidateVisual();
                return;
            }

            Point end = pos;
            if (_shiftPressed && _activeTool != ToolType.Pen)
            {
                if (_activeTool == ToolType.Line || _activeTool == ToolType.Arrow || _activeTool == ToolType.DoubleArrow)
                {
                    if (Math.Abs(end.X - _startPoint.X) > Math.Abs(end.Y - _startPoint.Y))
                        end.Y = _startPoint.Y;
                    else
                        end.X = _startPoint.X;
                }
                else
                {
                    double d = Math.Max(Math.Abs(end.X - _startPoint.X), Math.Abs(end.Y - _startPoint.Y));
                    end.X = _startPoint.X + (end.X >= _startPoint.X ? d : -d);
                    end.Y = _startPoint.Y + (end.Y >= _startPoint.Y ? d : -d);
                }
            }

            if (_activeTool == ToolType.Pen && _currentItem is PenStroke ps)
            {
                ps.Points.Add(end);
                ps.UpdateBounds();
            }
            else if (_currentItem is ShapeItem si)
            {
                si.End = end;
                si.UpdateBounds();
            }

            InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!_drawing) return;
            _drawing = false;
            ReleaseMouseCapture();

            if (_isSnapMode && _snapDrawing)
            {
                _snapDrawing = false;
                FinishSnap();
                return;
            }

            if (_currentItem != null && _activeTool != ToolType.Move)
            {
                SaveState();
                _items.Add(_currentItem);
            }
            else if (_currentItem != null && _activeTool == ToolType.Move)
            {
                int insertIdx = Math.Clamp(_selectedIndex, 0, _items.Count);
                _items.Insert(insertIdx, _currentItem);
                _selectedIndex = insertIdx;
            }

            _currentItem = null;
            InvalidateVisual();
        }

        private void CreateTextEditor(Point pos)
        {
            var pen = _settings.Pens[_activePenIndex];
            Color activeColor = (Color)ColorConverter.ConvertFromString(pen.Color);

            _textEditor = new TextBox
            {
                FontFamily = new FontFamily(_settings.Text.FontFamily),
                FontSize = _settings.Text.Size,
                FontWeight = _settings.Text.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = _settings.Text.Italic ? FontStyles.Italic : FontStyles.Normal,
                Foreground = new SolidColorBrush(activeColor),
                Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F46E5")),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 100,
                MaxWidth = 400,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = false,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _textEditor.SetValue(Canvas.LeftProperty, pos.X);
            _textEditor.SetValue(Canvas.TopProperty, pos.Y);
            DrawCanvas.Children.Add(_textEditor);
            _textEditor.Focus();
            _textEditor.LostFocus += TextEditor_LostFocus;
            _textEditor.KeyDown += TextEditor_KeyDown;
        }

        private void TextEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTextEditor();
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEditor();
            }
        }

        private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitTextEditor();
        }

        private void CommitTextEditor()
        {
            if (_textEditor == null) return;
            string text = _textEditor.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                SaveState();
                var pen = _settings.Pens[_activePenIndex];
                Color activeColor = (Color)ColorConverter.ConvertFromString(pen.Color);
                
                var pos = new Point((double)_textEditor.GetValue(Canvas.LeftProperty), (double)_textEditor.GetValue(Canvas.TopProperty));
                var item = new TextItem
                {
                    Text = text,
                    Position = pos,
                    FontFamily = _settings.Text.FontFamily,
                    FontSize = _settings.Text.Size,
                    Bold = _settings.Text.Bold,
                    Italic = _settings.Text.Italic,
                    Color = activeColor,
                    Alpha = 1.0
                };
                item.UpdateBounds();
                _items.Add(item);
            }
            CancelTextEditor();
            InvalidateVisual();
            SetTool(ToolType.Pen);
        }

        private void CancelTextEditor()
        {
            if (_textEditor != null)
            {
                DrawCanvas.Children.Remove(_textEditor);
                _textEditor.LostFocus -= TextEditor_LostFocus;
                _textEditor.KeyDown -= TextEditor_KeyDown;
                _textEditor = null;
            }
        }


        private void FinishSnap()
        {
            if (_snapStartRect.Width < 5 || _snapStartRect.Height < 5)
            {
                // Tiny drag or click — stay in snap mode, ready for next drag
                _snapDrawing = false;
                _snapStartRect = new Rect();
                InvalidateVisual();
                return;
            }

            var dpiScale = ScreenCapture.GetDpiScale();
            var captureRect = new Rect(
                Left + _snapStartRect.X,
                Top + _snapStartRect.Y,
                _snapStartRect.Width,
                _snapStartRect.Height);

            // Use opacity=0 instead of Hide() to avoid flicker — matches HTML's setOpacity(0) trick
            SetClickThrough(true);
            Opacity = 0;
            _toolbar.Opacity = 0;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    string savePath = SettingsStore.GetSavePath(_settings);
                    string file = Path.Combine(savePath, $"Simpink_Snap_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    ScreenCapture.CaptureToPng(file, captureRect);
                    ScreenCapture.OpenInExplorer(file);
                }
                finally
                {
                    Opacity = 1;
                    _toolbar.Opacity = 1;
                    SetClickThrough(false);
                    // Stay in snap mode (match HTML: isPartialSnapMode stays true)
                    // Just reset the drawing state so user can drag again immediately
                    _snapDrawing = false;
                    _snapStartRect = new Rect();
                    Cursor = Cursors.Cross;
                    InvalidateVisual();
                }
            }, DispatcherPriority.Render);
        }

        private int HitTest(Point p)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].HitTest(p)) return i;
            }
            return -1;
        }

        private void SaveState()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_items);
            _undoStack.Push(json);
            if (_undoStack.Count > 20) { var arr = _undoStack.ToArray(); Array.Reverse(arr); _undoStack.Clear(); foreach (var s in arr.Take(20)) _undoStack.Push(s); }
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(System.Text.Json.JsonSerializer.Serialize(_items));
            var json = _undoStack.Pop();
            _items.Clear();
            _items.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<DrawItem>>(json)!);
            InvalidateVisual();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(System.Text.Json.JsonSerializer.Serialize(_items));
            var json = _redoStack.Pop();
            _items.Clear();
            _items.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<DrawItem>>(json)!);
            InvalidateVisual();
        }

        public void CloneLast()
        {
            if (_items.Count == 0) return;
            SaveState();
            var clone = _items[^1].Clone();
            clone.Translate(new Vector(30, 30));
            _items.Add(clone);
            InvalidateVisual();
        }

        public void ClearCanvas()
        {
            if (_items.Count == 0) return;
            SaveState();
            _items.Clear();
            InvalidateVisual();
        }

        public void SetTool(ToolType tool)
        {
            _activeTool = tool;
            _isSnapMode = tool == ToolType.Snap;
            _selectedIndex = -1;

            if (_isSnapMode)
            {
                // Snap: needs mouse events but keeps pointer-mode = true for click-through logic
                _isPointerMode = true;
                _snapDrawing = false;
                _snapStartRect = new Rect();
            }
            else if (tool == ToolType.Pen || tool == ToolType.Eraser || tool == ToolType.Text ||
                tool == ToolType.Line || tool == ToolType.Rect || tool == ToolType.Circle ||
                tool == ToolType.Arrow || tool == ToolType.DoubleArrow || tool == ToolType.Move)
            {
                _isPointerMode = false;
            }
            else
            {
                _isPointerMode = true;
            }

            SetClickThrough(_isPointerMode && !_isSnapMode);
            // Snap mode: crosshair. Text: IBeam. Drawing tools: crosshair. Pointer: arrow.
            Cursor = _isSnapMode ? Cursors.Cross
                   : _activeTool == ToolType.Text ? Cursors.IBeam
                   : _isPointerMode ? Cursors.Arrow
                   : Cursors.Cross;
            _toolbar.SetActiveTool(tool);
            _toolbar.SetCanvasMode(!_isPointerMode);
            InvalidateVisual();
        }

        public void SetPointerMode()
        {
            _isPointerMode = true;
            _isSnapMode = false;
            _activeTool = ToolType.Pen;
            _selectedIndex = -1;
            _bgMode = BackgroundMode.None;
            BackgroundOverlay.Background = Brushes.Transparent;
            BackgroundOverlay.Effect = null;
            UpdateHaloPosition();   // snap the halo to the live cursor immediately
            SetClickThrough(true);
            Cursor = Cursors.Arrow;
            _toolbar.SetPointerMode();
            _toolbar.SetCanvasMode(false);
            InvalidateVisual();
        }

        public void SetPenWidth(double w) { _penWidth = w; _settings.PenWidth = w; }

        public void SetActivePen(int index)
        {
            if (index >= 0 && index < _settings.Pens.Count)
            {
                _activePenIndex = index;
                ApplyPenSettings();
            }
        }

        public void SetBackground(BackgroundMode mode)
        {
            _bgMode = mode;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            switch (_bgMode)
            {
                case BackgroundMode.None:
                    BackgroundOverlay.Background = Brushes.Transparent;
                    BackgroundOverlay.Effect = null;
                    break;
                case BackgroundMode.White:
                    BackgroundOverlay.Background = Brushes.White;
                    BackgroundOverlay.Effect = null;
                    break;
                case BackgroundMode.Dark:
                    BackgroundOverlay.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                    BackgroundOverlay.Effect = null;
                    break;
                case BackgroundMode.Blur:
                    CaptureBlurBackground(24);
                    break;
            }
        }

        private async void CaptureBlurBackground(int blurRadius = 20)
        {
            SetClickThrough(true);
            if (_toolbar != null) _toolbar.Hide();
            Hide();
            await Task.Delay(150); // give OS time to fully hide before capture
            bool captureSuccess = false;
            try
            {
                using var rawBmp = ScreenCapture.CaptureScreen();
                if (rawBmp != null)
                {
                    using var blurredBmp = ScreenCapture.FastBlur(rawBmp, blurRadius);
                    _blurBackground = ScreenCapture.ToBitmapSource(blurredBmp);
                    BackgroundOverlay.Background = new ImageBrush(_blurBackground) { Stretch = Stretch.UniformToFill };
                    BackgroundOverlay.Effect = null;
                    captureSuccess = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Blur background capture error: " + ex.Message);
            }
            finally
            {
                Show();
                if (_toolbar != null) _toolbar.Show();

                if (captureSuccess)
                {
                    // Enter drawing mode so the overlay is non-click-through and
                    // the blurred background is actually visible + annotatable.
                    _isPointerMode = false;
                    _activeTool = ToolType.Pen;
                    Cursor = Cursors.Cross;
                    _toolbar?.SetActiveTool(ToolType.Pen);
                    _toolbar?.SetCanvasMode(true);
                    SetClickThrough(false);
                }
                else
                {
                    SetClickThrough(_isPointerMode);
                }
            }
        }

        public void StartSnap() => SetTool(ToolType.Snap);

        public async void ToggleRecording()
        {
            if (_recording)
            {
                StopRecording();
            }
            else
            {
                await StartRecording();
            }
        }

        private async Task StartRecording()
        {
            if (!Recorder.IsFfmpegAvailable())
            {
                var result = MessageBox.Show(
                    "FFmpeg is required for screen recording, but it is not installed on your system.\n\n" +
                    "Would you like Simpink to automatically download and install it now (approx 120MB)?",
                    "Download FFmpeg", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    
                if (result == MessageBoxResult.Yes)
                {
                    string targetFfmpeg = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Simpink", "ffmpeg.exe");
                    var dialog = new DownloadDialog(targetFfmpeg);
                    dialog.ShowDialog();
                    
                    if (!dialog.DownloadSuccessful || !Recorder.IsFfmpegAvailable())
                    {
                        MessageBox.Show("FFmpeg download failed or was cancelled.", "Download Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    return; // User declined
                }
            }

            var bounds = ScreenCapture.GetPrimaryScreenBounds();
            double dpiScale = ScreenCapture.GetDpiScale();
            int w = (int)Math.Round(bounds.Width * dpiScale);
            int h = (int)Math.Round(bounds.Height * dpiScale);
            if (w % 2 == 1) w--;
            if (h % 2 == 1) h--;

            _recordingBounds = bounds;
            var quality = VideoQualityHelper.Parse(_settings.VideoQuality);
            int bitrate = VideoQualityHelper.GetBitrate(quality);

            string savePath = SettingsStore.GetSavePath(_settings);
            // Save as .webm (FFmpeg VP9, matching HTML quality settings)
            string file = Path.Combine(savePath, $"Simpink_Rec_{DateTime.Now:yyyyMMdd_HHmmss}.webm");

            _recorder = new Recorder(w, h, 30, bitrate);
            if (!_recorder.Start(file))
            {
                _recorder.Dispose();
                _recorder = null;
                MessageBox.Show("Failed to start recording. Make sure FFmpeg is installed (it was found but may have an issue).", "Simpink Recording Error");
                return;
            }

            _recording = true;
            _recordingPaused = false;
            _toolbar.SetRecordingState(true, false);

            _recordCts = new CancellationTokenSource();
            _recordThread = new Thread(() => RecordLoop(_recordCts.Token)) { IsBackground = true };
            _recordThread.Start();
        }

        private void RecordLoop(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            double frameDurationMs = 1000.0 / 30.0;
            long frameCount = 0;
            long pausedTime = 0;
            bool wasPaused = false;
            long pauseStart = 0;

            while (!token.IsCancellationRequested && _recording)
            {
                if (_recordingPaused)
                {
                    if (!wasPaused)
                    {
                        wasPaused = true;
                        pauseStart = sw.ElapsedMilliseconds;
                    }
                    Thread.Sleep(50);
                    continue;
                }
                
                if (wasPaused)
                {
                    wasPaused = false;
                    pausedTime += (sw.ElapsedMilliseconds - pauseStart);
                }

                long effectiveTime = sw.ElapsedMilliseconds - pausedTime;
                long expectedFrames = (long)(effectiveTime / frameDurationMs);
                
                if (frameCount <= expectedFrames)
                {
                    try
                    {
                        using var bmp = ScreenCapture.CaptureScreen(_recordingBounds);
                        if (bmp != null)
                        {
                            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            
                            // If we fell behind, write the frame multiple times to maintain 30 FPS sync
                            int framesToWrite = (int)(expectedFrames - frameCount) + 1;
                            if (framesToWrite > 15) framesToWrite = 15; // cap catch-up to prevent death spirals

                            for (int i = 0; i < framesToWrite; i++)
                            {
                                if (token.IsCancellationRequested) break;
                                _recorder?.WriteFrame(data.Scan0);
                                frameCount++;
                            }
                            
                            bmp.UnlockBits(data);
                        }
                        else
                        {
                            frameCount++;
                        }
                    }
                    catch { frameCount++; }
                }
                else
                {
                    int sleep = (int)((frameCount * frameDurationMs) - effectiveTime);
                    if (sleep > 0) Thread.Sleep(Math.Min(sleep, 15));
                }
            }
        }

        public void PauseRecording()
        {
            if (!_recording || _recorder == null) return;
            if (_recordingPaused)
            {
                _recorder.Resume();
                _recordingPaused = false;
            }
            else
            {
                _recorder.Pause();
                _recordingPaused = true;
            }
            _toolbar.SetRecordingState(_recording, _recordingPaused);
        }

        private void StopRecording()
        {
            string savedPath = _recorder?.OutputPath ?? "";
            _recordCts?.Cancel();
            _recordThread?.Join(1000); // Wait up to 1s for thread to finish

            // Stop() blocks until FFmpeg finalizes the MP4 (moov atom written)
            _recorder?.Stop();
            _recorder?.Dispose();
            _recorder = null;
            _recording = false;
            _recordingPaused = false;
            _toolbar.SetRecordingState(false, false);

            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
            {
                MessageBox.Show($"Recording saved!\n{savedPath}", "Simpink",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ScreenCapture.OpenInExplorer(savedPath);
            }
            else
            {
                MessageBox.Show("Recording saved!", "Simpink");
            }
        }

        public void UpdatePenConfig(List<PenConfig> pens) => _settings.Pens = pens;

        public void UpdateTextConfig(TextConfig tc) => _settings.Text = tc;

        public void UpdateVideoQuality(string vq) => _settings.VideoQuality = vq;

        public void SetSavePath(string path) => _settings.SavePath = path;

        public void ResetDefaults()
        {
            _settings = new Settings();
            _activePenIndex = 0;
            _penWidth = 3;
            ApplyPenSettings();
            _toolbar.UpdatePenUI(_settings.Pens, _activePenIndex);
            _toolbar.UpdatePenWidth(_penWidth);
            _toolbar.UpdateTextConfig(_settings.Text);
            _toolbar.UpdateVideoQuality(_settings.VideoQuality);
            SaveSettings();
        }

        private void OnHotkey(int id)
        {
            Dispatcher.Invoke(() =>
            {
                if (id == 1) ToggleRecording();
                else if (id == 2) PauseRecording();
            });
        }

        internal void RenderOverlay(DrawingContext dc)
        {
            foreach (var item in _items)
                item.Draw(dc);

            if (_currentItem != null && _drawing)
                _currentItem.Draw(dc);

            if (_isPointerMode && !_isSnapMode)
            {
                var pt = Win32.GetCursorPosition();
                var dpiScale = ScreenCapture.GetDpiScale();
                _mousePos = new Point(pt.X / dpiScale - Left, pt.Y / dpiScale - Top);

                var haloBrush = new SolidColorBrush(Color.FromArgb(90, 251, 191, 36));
                haloBrush.Freeze();
                var haloPen = new Pen(new SolidColorBrush(Color.FromArgb(128, 251, 191, 36)), 2);
                haloPen.Freeze();
                dc.DrawEllipse(haloBrush, haloPen, _mousePos, 25, 25);
            }

            if (_isSnapMode)
            {
                // Always show dim when in snap mode (even before drag starts)
                // Use 4-rect technique to carve a true transparent hole in the dim
                var dimBrush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
                dimBrush.Freeze();

                if (_snapDrawing && _snapStartRect.Width > 0 && _snapStartRect.Height > 0)
                {
                    // Carve out the selection by drawing 4 rects around it
                    var r = _snapStartRect;
                    double W = ActualWidth, H = ActualHeight;

                    // Top strip
                    if (r.Top > 0)
                        dc.DrawRectangle(dimBrush, null, new Rect(0, 0, W, r.Top));
                    // Bottom strip
                    if (r.Bottom < H)
                        dc.DrawRectangle(dimBrush, null, new Rect(0, r.Bottom, W, H - r.Bottom));
                    // Left strip (between top and bottom)
                    if (r.Left > 0)
                        dc.DrawRectangle(dimBrush, null, new Rect(0, r.Top, r.Left, r.Height));
                    // Right strip (between top and bottom)
                    if (r.Right < W)
                        dc.DrawRectangle(dimBrush, null, new Rect(r.Right, r.Top, W - r.Right, r.Height));

                    // Dashed border — white outer, indigo inner (same as HTML)
                    var borderPen = new Pen(Brushes.White, 2) { DashStyle = DashStyles.Dash };
                    borderPen.Freeze();
                    var borderPen2 = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0x46, 0xE5)), 1);
                    borderPen2.Freeze();
                    dc.DrawRectangle(null, borderPen, r);
                    dc.DrawRectangle(null, borderPen2, r);
                }
                else
                {
                    // No drag yet — just dim the whole screen
                    dc.DrawRectangle(dimBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
                }
            }

            if (_activeTool == ToolType.Move)
            {
                DrawItem? selItem = null;
                if (_drawing && _currentItem != null)
                {
                    selItem = _currentItem;
                }
                else if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    selItem = _items[_selectedIndex];
                }

                if (selItem != null)
                {
                    var margin = 5;
                    var selRect = new Rect(selItem.Bounds.Left - margin, selItem.Bounds.Top - margin,
                        selItem.Bounds.Width + margin * 2, selItem.Bounds.Height + margin * 2);
                    var pen = new Pen(Brushes.Indigo, 1.5) { DashStyle = DashStyles.Dash };
                    pen.Freeze();
                    dc.DrawRectangle(null, pen, selRect);
                }
            }
        }
    }

    public class OverlayDrawCanvas : Canvas
    {
        public OverlayWindow? Owner { get; set; }
        private bool _traced;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!_traced)
            {
                _traced = true;
                try
                {
                    File.AppendAllText(@"C:\Users\Asus\AppData\Local\Temp\commandcode\C--Users-Asus-Desktop-COMMANDCODE-PROJECTS\Wab0QQa-fMCxmJ9N1VrBh\scratchpad\render_trace.txt",
                        $"[{DateTime.Now:HH:mm:ss.fff}] OnRender fired, size={ActualWidth}x{ActualHeight}, owner={(Owner != null)}\n");
                }
                catch { }
            }
            Owner?.RenderOverlay(dc);
        }
    }
}