using System;
using System.IO;
using System.Text.Json;
using SimpinkNative.Models;

namespace SimpinkNative.Services
{
    public static class SettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpinkNative", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<Settings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new Settings();
        }

        public static void Save(Settings s)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static string GetSavePath(Settings s)
        {
            if (!string.IsNullOrEmpty(s.SavePath) && Directory.Exists(s.SavePath))
                return s.SavePath;
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
    }
}