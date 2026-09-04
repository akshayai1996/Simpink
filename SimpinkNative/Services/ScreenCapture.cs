using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SimpinkNative.Services
{
    public static class ScreenCapture
    {
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight,
            IntPtr hSrcDC, int xSrc, int ySrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        private const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public static Bitmap CaptureScreen(Rect? regionDip = null)
        {
            var bounds = regionDip ?? GetPrimaryScreenBounds();
            double dpiScale = GetDpiScale();
            
            int x = (int)Math.Round(bounds.X * dpiScale);
            int y = (int)Math.Round(bounds.Y * dpiScale);
            int w = (int)Math.Round(bounds.Width * dpiScale);
            int h = (int)Math.Round(bounds.Height * dpiScale);

            if (w <= 0 || h <= 0) return null!;

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, w, h);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            bool ok = BitBlt(hdcMem, 0, 0, w, h, hdcScreen, x, y, SRCCOPY);

            SelectObject(hdcMem, hOld);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            if (!ok)
            {
                DeleteObject(hBitmap);
                return null!;
            }

            var bmp = Bitmap.FromHbitmap(hBitmap);
            DeleteObject(hBitmap);
            return bmp;
        }

        public static void CaptureToPng(string path, Rect? regionDip = null)
        {
            using var bmp = CaptureScreen(regionDip);
            bmp?.Save(path, ImageFormat.Png);
        }

        public static BitmapSource CaptureToBitmapSource(Rect? regionDip = null)
        {
            using var bmp = CaptureScreen(regionDip);
            if (bmp == null) return null!;
            return ToBitmapSource(bmp);
        }

        public static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            if (bmp == null) return null!;
            var hBitmap = bmp.GetHbitmap();
            try
            {
                var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        public static Bitmap FastBlur(Bitmap image, int blurRadius)
        {
            if (image == null) return null!;
            int scaleFactor = blurRadius > 15 ? 4 : 2;
            int lowW = Math.Max(16, image.Width / scaleFactor);
            int lowH = Math.Max(16, image.Height / scaleFactor);

            using var lowResBmp = new Bitmap(lowW, lowH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(lowResBmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, 0, 0, lowW, lowH);
            }

            BoxBlur(lowResBmp, Math.Max(1, blurRadius / scaleFactor));

            var result = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(lowResBmp, 0, 0, image.Width, image.Height);
            }

            return result;
        }

        private static void BoxBlur(Bitmap bmp, int range)
        {
            if (range < 1) range = 1;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int width = bmp.Width;
            int height = bmp.Height;
            int stride = data.Stride;
            int bytes = stride * height;
            byte[] pixelBuffer = new byte[bytes];
            byte[] resultBuffer = new byte[bytes];

            Marshal.Copy(data.Scan0, pixelBuffer, 0, bytes);

            for (int y = 0; y < height; y++)
            {
                int rAcc = 0, gAcc = 0, bAcc = 0, aAcc = 0;
                int count = 0;
                int rowStart = y * stride;

                for (int x = -range; x < width + range; x++)
                {
                    if (x + range < width)
                    {
                        int pIn = rowStart + (x + range) * 4;
                        bAcc += pixelBuffer[pIn];
                        gAcc += pixelBuffer[pIn + 1];
                        rAcc += pixelBuffer[pIn + 2];
                        aAcc += pixelBuffer[pIn + 3];
                        count++;
                    }
                    if (x - range >= 0)
                    {
                        int pOut = rowStart + (x - range) * 4;
                        bAcc -= pixelBuffer[pOut];
                        gAcc -= pixelBuffer[pOut + 1];
                        rAcc -= pixelBuffer[pOut + 2];
                        aAcc -= pixelBuffer[pOut + 3];
                        count--;
                    }
                    if (x >= 0 && x < width)
                    {
                        int pCurrent = rowStart + x * 4;
                        resultBuffer[pCurrent] = (byte)(bAcc / count);
                        resultBuffer[pCurrent + 1] = (byte)(gAcc / count);
                        resultBuffer[pCurrent + 2] = (byte)(rAcc / count);
                        resultBuffer[pCurrent + 3] = (byte)(aAcc / count);
                    }
                }
            }

            for (int x = 0; x < width; x++)
            {
                int rAcc = 0, gAcc = 0, bAcc = 0, aAcc = 0;
                int count = 0;

                for (int y = -range; y < height + range; y++)
                {
                    if (y + range < height)
                    {
                        int pIn = (y + range) * stride + x * 4;
                        bAcc += resultBuffer[pIn];
                        gAcc += resultBuffer[pIn + 1];
                        rAcc += resultBuffer[pIn + 2];
                        aAcc += resultBuffer[pIn + 3];
                        count++;
                    }
                    if (y - range >= 0)
                    {
                        int pOut = (y - range) * stride + x * 4;
                        bAcc -= resultBuffer[pOut];
                        gAcc -= resultBuffer[pOut + 1];
                        rAcc -= resultBuffer[pOut + 2];
                        aAcc -= resultBuffer[pOut + 3];
                        count--;
                    }
                    if (y >= 0 && y < height)
                    {
                        int pCurrent = y * stride + x * 4;
                        pixelBuffer[pCurrent] = (byte)(bAcc / count);
                        pixelBuffer[pCurrent + 1] = (byte)(gAcc / count);
                        pixelBuffer[pCurrent + 2] = (byte)(rAcc / count);
                        pixelBuffer[pCurrent + 3] = (byte)(aAcc / count);
                    }
                }
            }

            Marshal.Copy(pixelBuffer, 0, data.Scan0, bytes);
            bmp.UnlockBits(data);
        }

        public static Rect GetPrimaryScreenBounds()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        public static double GetDpiScale()
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }

        public static void OpenInExplorer(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch { }
        }
    }
}
