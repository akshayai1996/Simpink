using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SimpinkNative.Services
{
    /// <summary>
    /// Minimal, dependency-free AVI (Video DIB, uncompressed RGB24) writer.
    /// Used as a universal fallback when Media Foundation H.264 encoding is
    /// unavailable on the host machine.
    /// </summary>
    public sealed class AviWriter : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly int _stride;          // source BGRA stride (bytes)
        private readonly int _rowBytes;        // destination BGR row size
        private readonly FileStream _fs;
        private readonly BinaryWriter _w;
        private long _frameCount;
        private long _moviStart;               // file pos of movi size field
        private long _framesStart;             // file pos of first frame chunk
        private long _posRiffSize;
        private long _posAvihFrames;
        private long _posStrlLength;
        private readonly List<(uint Offset, uint Size)> _index = new();
        private bool _disposed;

        public AviWriter(string path, int width, int height, int fps, int bgraStride)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _stride = bgraStride;
            _rowBytes = width * 3;

            _fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _w = new BinaryWriter(_fs);
            WriteHeader();
        }

        private void WriteHeader()
        {
            var ascii = Encoding.ASCII;

            _w.Write(ascii.GetBytes("RIFF"));
            _posRiffSize = _fs.Position;
            _w.Write(0u);                                     // patched on Stop
            _w.Write(ascii.GetBytes("AVI "));

            // ---- LIST hdrl ----
            _w.Write(ascii.GetBytes("LIST"));
            _w.Write(192u);                                   // hdrl payload (see below)
            _w.Write(ascii.GetBytes("hdrl"));

            // avih: 56 bytes
            _w.Write(ascii.GetBytes("avih"));
            _w.Write(56u);
            _w.Write(1000000u / (uint)_fps);                  // dwMicroSecPerFrame
            _w.Write(0u);                                     // dwMaxBytesPerSec
            _w.Write(0u);                                     // dwPaddingGranularity
            _w.Write(0x00000110u);                            // AVIF_HASINDEX|AVIF_ISINTERLEAVED
            _posAvihFrames = _fs.Position;
            _w.Write(0u);                                     // dwTotalFrames (patched)
            _w.Write(0u);                                     // dwInitialFrames
            _w.Write(1u);                                     // dwStreams
            _w.Write((uint)(_rowBytes * _height));            // dwSuggestedBufferSize
            _w.Write(_width);                                 // dwWidth
            _w.Write(_height);                                // dwHeight
            _w.Write(0u);
            _w.Write(0u);                                     // dwReserved[2]

            // ---- LIST strl ----
            _w.Write(ascii.GetBytes("LIST"));
            _w.Write(116u);                                   // strl payload: 4(strl)+64(strh)+48(strf)
            _w.Write(ascii.GetBytes("strl"));

            // strh: 56 bytes
            _w.Write(ascii.GetBytes("strh"));
            _w.Write(56u);
            _w.Write(ascii.GetBytes("vids"));
            _w.Write(ascii.GetBytes("DIB "));
            _w.Write(0u);                                     // dwFlags
            _w.Write((ushort)0);                              // wPriority
            _w.Write((ushort)0);                              // wLanguage
            _w.Write(0u);                                     // dwInitialFrames
            _w.Write(1u);                                     // dwScale
            _w.Write((uint)_fps);                             // dwRate
            _posStrlLength = _fs.Position;
            _w.Write(0u);                                     // dwLength (patched)
            _w.Write((uint)(_rowBytes * _height));            // dwSuggestedBufferSize
            _w.Write(0xFFFFFFFFu);                            // dwQuality
            _w.Write(0u);                                     // dwSampleSize
            _w.Write(0);                                      // rcFrame.left
            _w.Write(0);                                      // rcFrame.top
            _w.Write(_width);                                 // rcFrame.right
            _w.Write(_height);                                // rcFrame.bottom

            // strf: BITMAPINFOHEADER (40 bytes)
            _w.Write(ascii.GetBytes("strf"));
            _w.Write(40u);
            _w.Write(40u);                                    // biSize
            _w.Write(_width);
            _w.Write(_height);                                // positive = bottom-up
            _w.Write((ushort)1);                              // biPlanes
            _w.Write((ushort)24);                             // biBitCount
            _w.Write(0u);                                     // biCompression = BI_RGB
            _w.Write((uint)(_rowBytes * _height));            // biSizeImage
            _w.Write(0);
            _w.Write(0);
            _w.Write(0);
            _w.Write(0);

            // ---- movi ----
            _w.Write(ascii.GetBytes("LIST"));
            _moviStart = _fs.Position;
            _w.Write(0u);                                     // movi payload size (patched)
            _w.Write(ascii.GetBytes("movi"));
            _framesStart = _fs.Position;
        }

        public void WriteFrame(IntPtr bgraData)
        {
            if (_disposed) return;
            unsafe
            {
                byte* src = (byte*)bgraData;
                byte[] row = new byte[_rowBytes];
                _index.Add(((uint)(_fs.Position - _framesStart), (uint)_rowBytes * (uint)_height));
                _w.Write(Encoding.ASCII.GetBytes("00db"));
                _w.Write((uint)(_rowBytes * _height));
                for (int y = 0; y < _height; y++)
                {
                    // GDI/GDI+ bitmaps are top-down: flip to bottom-up for AVI DIB
                    int srcY = y * _stride;
                    byte* p = src + srcY;
                    for (int x = 0; x < _width; x++)
                    {
                        row[x * 3] = p[x * 4 + 0];             // B
                        row[x * 3 + 1] = p[x * 4 + 1];         // G
                        row[x * 3 + 2] = p[x * 4 + 2];         // R
                    }
                    _w.Write(row);
                }
                if ((_rowBytes * _height) % 2 == 1) _w.Write((byte)0);
            }
            _frameCount++;
        }

        public void Stop()
        {
            if (_disposed) return;

            long idxStart = _fs.Position;
            _w.Write(Encoding.ASCII.GetBytes("idx1"));
            _w.Write((uint)(_index.Count * 16));
            foreach (var (off, size) in _index)
            {
                _w.Write(Encoding.ASCII.GetBytes("00db"));
                _w.Write(0x00000010u);                        // AVIIF_KEYFRAME
                _w.Write(off);
                _w.Write(size);
            }

            long endPos = _fs.Position;
            _fs.Position = _posRiffSize;
            _w.Write((uint)(endPos - 8));                      // RIFF size
            _fs.Position = _posAvihFrames;
            _w.Write((uint)_frameCount);                       // dwTotalFrames
            _fs.Position = _posStrlLength;
            _w.Write((uint)_frameCount);                       // strh dwLength
            _fs.Position = _moviStart;
            _w.Write((uint)(idxStart - _moviStart - 4));       // movi payload size
            _fs.Position = endPos;

            _w.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _w.Dispose();
            _fs.Dispose();
            _disposed = true;
        }
    }
}