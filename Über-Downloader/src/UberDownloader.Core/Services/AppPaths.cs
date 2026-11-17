using System;
using System.IO;

namespace UberDownloader.Core.Services;

public static class AppPaths
{
    private static string? _dataDirectory;

    public static string DataDirectory
    {
        get
        {
            if (_dataDirectory != null) return _dataDirectory;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string portableFlag = Path.Combine(baseDir, "portable.flag");

            if (File.Exists(portableFlag))
            {
                _dataDirectory = Path.Combine(baseDir, "data");
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string newDir = Path.Combine(appData, "The-Whatever-Downloader");
                string oldDir = Path.Combine(appData, "Über-Downloader");

                if (!Directory.Exists(newDir) && Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.Move(oldDir, newDir);
                    }
                    catch { }
                }

                _dataDirectory = Directory.Exists(newDir) ? newDir : (Directory.Exists(oldDir) ? oldDir : newDir);
            }

            try
            {
                Directory.CreateDirectory(_dataDirectory);
            }
            catch { }

            return _dataDirectory;
        }
    }

    public static bool IsPortable => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.flag"));
}
