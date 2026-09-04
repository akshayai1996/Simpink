using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace SimpinkNative.Models
{
    public enum ToolType
    {
        Pen, Eraser, Text, Line, Rect, Circle, Arrow, DoubleArrow, Move, Snap
    }

    public enum BackgroundMode
    {
        None, White, Dark, Blur
    }

    [JsonPolymorphic]
    [JsonDerivedType(typeof(PenStroke), "pen")]
    [JsonDerivedType(typeof(LineItem), "line")]
    [JsonDerivedType(typeof(ArrowItem), "arrow")]
    [JsonDerivedType(typeof(DoubleArrowItem), "doublearrow")]
    [JsonDerivedType(typeof(RectItem), "rect")]
    [JsonDerivedType(typeof(CircleItem), "circle")]
    [JsonDerivedType(typeof(TextItem), "text")]
    public abstract class DrawItem
    {
        public ToolType Type { get; protected set; }
        public Rect Bounds { get; protected set; }
        public System.Windows.Media.Color Color { get; set; }
        public double Alpha { get; set; } = 1.0;
        public double Width { get; set; } = 3.0;

        public abstract void Draw(DrawingContext dc);
        public abstract bool HitTest(System.Windows.Point p, double tolerance = 15);
        public abstract DrawItem Clone();
        public abstract void Translate(Vector offset);
        public abstract void UpdateBounds();
    }

    public class PenStroke : DrawItem
    {
        public List<System.Windows.Point> Points { get; set; } = new();

        public PenStroke() { Type = ToolType.Pen; }

        public override void Draw(DrawingContext dc)
        {
            if (Points.Count < 2) return;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Points[0], false, false);
                ctx.PolyLineTo(Points.GetRange(1, Points.Count - 1), true, true);
            }
            geometry.Freeze();
            var brush = new SolidColorBrush(Color) { Opacity = Alpha };
            brush.Freeze();
            var pen = new System.Windows.Media.Pen(brush, Width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        public override bool HitTest(System.Windows.Point p, double tolerance = 15)
        {
            for (int i = 0; i < Points.Count - 1; i++)
            {
                if (DistanceToSegment(p, Points[i], Points[i + 1]) <= tolerance)
                    return true;
            }
            return false;
        }

        private static double DistanceToSegment(System.Windows.Point p, System.Windows.Point a, System.Windows.Point b)
        {
            var ab = b - a;
            var ap = p - a;
            double denom = ab.X * ab.X + ab.Y * ab.Y;
            if (denom == 0) return (p - a).Length;
            double t = (ap.X * ab.X + ap.Y * ab.Y) / denom;
            t = Math.Max(0, Math.Min(1, t));
            var closest = new System.Windows.Point(a.X + t * ab.X, a.Y + t * ab.Y);
            return (p - closest).Length;
        }

        public override DrawItem Clone()
        {
            var c = new PenStroke { Points = new List<System.Windows.Point>(Points), Color = Color, Alpha = Alpha, Width = Width };
            c.UpdateBounds();
            return c;
        }

        public override void Translate(Vector offset)
        {
            for (int i = 0; i < Points.Count; i++)
                Points[i] += offset;
            UpdateBounds();
        }

        public override void UpdateBounds()
        {
            if (Points.Count == 0) { Bounds = Rect.Empty; return; }
            double minX = Points[0].X, maxX = Points[0].X, minY = Points[0].Y, maxY = Points[0].Y;
            foreach (var pt in Points)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public class ShapeItem : DrawItem
    {
        public System.Windows.Point Start { get; set; }
        public System.Windows.Point End { get; set; }

        protected ShapeItem(ToolType type) { Type = type; }

        protected virtual void DrawShape(DrawingContext dc, System.Windows.Media.Pen pen) { }

        public override void Draw(DrawingContext dc)
        {
            var brush = new SolidColorBrush(Color) { Opacity = Alpha };
            brush.Freeze();
            var pen = new System.Windows.Media.Pen(brush, Width) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            DrawShape(dc, pen);
            if (Type == ToolType.Arrow || Type == ToolType.DoubleArrow)
                DrawArrowHeads(dc, pen);
        }

        private void DrawArrowHeads(DrawingContext dc, System.Windows.Media.Pen pen)
        {
            double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
            double headLen = 15;
            if (Type == ToolType.Arrow || Type == ToolType.DoubleArrow)
                DrawArrowHead(dc, pen, End, angle, headLen);
            if (Type == ToolType.DoubleArrow)
                DrawArrowHead(dc, pen, Start, angle + Math.PI, headLen);
        }

        private void DrawArrowHead(DrawingContext dc, System.Windows.Media.Pen pen, System.Windows.Point tip, double angle, double headLen)
        {
            var pts = new System.Windows.Point[3];
            pts[0] = tip;
            pts[1] = new System.Windows.Point(tip.X - headLen * Math.Cos(angle - Math.PI / 6), tip.Y - headLen * Math.Sin(angle - Math.PI / 6));
            pts[2] = new System.Windows.Point(tip.X - headLen * Math.Cos(angle + Math.PI / 6), tip.Y - headLen * Math.Sin(angle + Math.PI / 6));
            var geom = new StreamGeometry();
            using (var ctx = geom.Open()) { ctx.BeginFigure(pts[0], true, true); ctx.PolyLineTo(new[] { pts[1], pts[2] }, true, true); }
            geom.Freeze();
            dc.DrawGeometry(pen.Brush, null, geom);
        }

        public override bool HitTest(System.Windows.Point p, double tolerance = 15)
        {
            return p.X >= Bounds.Left - tolerance && p.X <= Bounds.Right + tolerance &&
                   p.Y >= Bounds.Top - tolerance && p.Y <= Bounds.Bottom + tolerance;
        }

        public override void Translate(Vector offset)
        {
            Start += offset;
            End += offset;
            UpdateBounds();
        }

        public override void UpdateBounds()
        {
            double minX = Math.Min(Start.X, End.X), maxX = Math.Max(Start.X, End.X);
            double minY = Math.Min(Start.Y, End.Y), maxY = Math.Max(Start.Y, End.Y);
            double w = maxX - minX;
            double h = maxY - minY;
            if (w == 0) w = Width;
            if (h == 0) h = Width;
            Bounds = new Rect(minX, minY, w, h);
        }

        public override DrawItem Clone()
        {
            var c = (ShapeItem)MemberwiseClone();
            c.UpdateBounds();
            return c;
        }
    }

    public class LineItem : ShapeItem
    {
        public LineItem() : base(ToolType.Line) { }
        protected override void DrawShape(DrawingContext dc, Pen pen) => dc.DrawLine(pen, Start, End);
    }

    public class ArrowItem : ShapeItem
    {
        public ArrowItem() : base(ToolType.Arrow) { }
        protected override void DrawShape(DrawingContext dc, Pen pen) => dc.DrawLine(pen, Start, End);
    }

    public class DoubleArrowItem : ShapeItem
    {
        public DoubleArrowItem() : base(ToolType.DoubleArrow) { }
        protected override void DrawShape(DrawingContext dc, Pen pen) => dc.DrawLine(pen, Start, End);
    }

    public class RectItem : ShapeItem
    {
        public RectItem() : base(ToolType.Rect) { }
        protected override void DrawShape(DrawingContext dc, Pen pen) => dc.DrawRectangle(null, pen, new Rect(Start, End));
    }

    public class CircleItem : ShapeItem
    {
        public CircleItem() : base(ToolType.Circle) { }
        protected override void DrawShape(DrawingContext dc, Pen pen)
        {
            double r = Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
            dc.DrawEllipse(null, pen, Start, r, r);
        }
    }

    public class TextItem : DrawItem
    {
        public string Text { get; set; } = "";
        public System.Windows.Point Position { get; set; }
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 30;
        public bool Bold { get; set; }
        public bool Italic { get; set; }

        public TextItem() { Type = ToolType.Text; }

        private static double GetPixelsPerDip()
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                    return VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
            }
            catch { }
            return 1.0;
        }

        public override void Draw(DrawingContext dc)
        {
            var ft = new FormattedText(Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new System.Windows.Media.FontFamily(FontFamily), Italic ? FontStyles.Italic : FontStyles.Normal, Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                FontSize,
                new SolidColorBrush(Color) { Opacity = Alpha },
                GetPixelsPerDip());
            dc.DrawText(ft, Position);
        }

        public override bool HitTest(System.Windows.Point p, double tolerance = 15)
        {
            return p.X >= Bounds.Left - tolerance && p.X <= Bounds.Right + tolerance &&
                   p.Y >= Bounds.Top - tolerance && p.Y <= Bounds.Bottom + tolerance;
        }

        public override DrawItem Clone()
        {
            var c = (TextItem)MemberwiseClone();
            c.UpdateBounds();
            return c;
        }

        public override void Translate(Vector offset)
        {
            Position += offset;
            UpdateBounds();
        }

        public override void UpdateBounds()
        {
            var ft = new FormattedText(Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new System.Windows.Media.FontFamily(FontFamily), Italic ? FontStyles.Italic : FontStyles.Normal, Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                FontSize,
                Brushes.Black,
                GetPixelsPerDip());
            Bounds = new Rect(Position.X, Position.Y, ft.Width, ft.Height);
        }
    }

    public class PenConfig
    {
        public string Color { get; set; } = "#EF4444";
        public int Alpha { get; set; } = 100;

        public PenConfig() { }
        public PenConfig(string color, int alpha) { Color = color; Alpha = alpha; }
    }

    public class TextConfig
    {
        public string FontFamily { get; set; } = "Segoe UI";
        public string Color { get; set; } = "#4F46E5";
        public double Size { get; set; } = 30;
        public bool Bold { get; set; } = true;
        public bool Italic { get; set; } = false;
    }

    public class Settings
    {
        public List<PenConfig> Pens { get; set; } = new()
        {
            new("#EF4444", 100), new("#22C55E", 100), new("#3B82F6", 100),
            new("#EF4444", 45), new("#22C55E", 45), new("#3B82F6", 45)
        };
        public TextConfig Text { get; set; } = new();
        public string VideoQuality { get; set; } = "hd";
        public string SavePath { get; set; } = "";
        public int ActivePenIndex { get; set; } = 0;
        public double PenWidth { get; set; } = 3;
    }

    public enum VideoQuality
    {
        Standard, HD, Pro
    }

    public static class VideoQualityHelper
    {
        public static int GetBitrate(VideoQuality q) => q switch
        {
            VideoQuality.Standard => 5_000_000,
            VideoQuality.HD => 10_000_000,
            VideoQuality.Pro => 25_000_000,
            _ => 10_000_000
        };

        public static VideoQuality Parse(string s) => s switch
        {
            "std" => VideoQuality.Standard,
            "pro" => VideoQuality.Pro,
            _ => VideoQuality.HD
        };

        public static string ToString(VideoQuality q) => q switch
        {
            VideoQuality.Standard => "std",
            VideoQuality.HD => "hd",
            VideoQuality.Pro => "pro",
            _ => "hd"
        };
    }
}