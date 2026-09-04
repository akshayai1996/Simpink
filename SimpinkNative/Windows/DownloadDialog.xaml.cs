using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace SimpinkNative.Windows
{
    public partial class DownloadDialog : Window
    {
        // GitHub release with a zip file for windows
        private const string FfmpegZipUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        
        public bool DownloadSuccessful { get; private set; }
        private readonly string _targetPath;

        public DownloadDialog(string targetPath)
        {
            InitializeComponent();
            _targetPath = targetPath;
            Loaded += DownloadDialog_Loaded;
        }

        private async void DownloadDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await DownloadAndExtractFfmpegAsync();
                DownloadSuccessful = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to download FFmpeg: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DownloadSuccessful = false;
                Close();
            }
        }

        private async Task DownloadAndExtractFfmpegAsync()
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_temp.zip");
            
            StatusText.Text = "Downloading FFmpeg (~120MB)...";
            
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SimpinkNativeDownloader");
                
                using (var response = await client.GetAsync(FfmpegZipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;
                        
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            
                            if (totalBytes != -1)
                            {
                                double percentage = (double)totalRead / totalBytes * 100;
                                ProgressBar.Value = percentage;
                                ProgressText.Text = $"{percentage:F1}%";
                            }
                        }
                    }
                }
            }

            StatusText.Text = "Extracting FFmpeg...";
            ProgressBar.IsIndeterminate = true;
            ProgressText.Text = "Please wait...";

            await Task.Run(() =>
            {
                // We only need bin/ffmpeg.exe
                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetDir = Path.GetDirectoryName(_targetPath);
                            if (targetDir != null && !Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            
                            // Delete if exists, then extract
                            if (File.Exists(_targetPath))
                            {
                                File.Delete(_targetPath);
                            }
                            
                            entry.ExtractToFile(_targetPath, true);
                            break;
                        }
                    }
                }
                
                // Cleanup temp zip
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            });
        }
    }
}
