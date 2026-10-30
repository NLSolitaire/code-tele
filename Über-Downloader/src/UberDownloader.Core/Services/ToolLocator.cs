using System;
using System.IO;

namespace UberDownloader.Core.Services;

public static class ToolLocator
{
    public static string GetYtDlpPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Dossier bin de l'application (prioritaire pour la distribution autonome)
        string appBin = Path.Combine(baseDir, "bin", "yt-dlp.exe");
        if (File.Exists(appBin)) return appBin;

        // 2. Racine de l'application
        string appRoot = Path.Combine(baseDir, "yt-dlp.exe");
        if (File.Exists(appRoot)) return appRoot;

        // 3. Dossier de données utilisateur (ex: %LOCALAPPDATA%\UberDownloader\bin)
        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "yt-dlp.exe");
        if (File.Exists(dataBin)) return dataBin;

        // 4. Dossier standard LocalAppData Programs
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progYtDlp = Path.Combine(localAppData, "Programs", "yt-dlp", "yt-dlp.exe");
        if (File.Exists(progYtDlp)) return progYtDlp;

        // 5. Fallback sur le PATH système
        return "yt-dlp";
    }

    public static string GetStreamlinkPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Dossier bin de l'application
        string appBin = Path.Combine(baseDir, "bin", "streamlink.exe");
        if (File.Exists(appBin)) return appBin;

        // 2. Racine de l'application
        string appRoot = Path.Combine(baseDir, "streamlink.exe");
        if (File.Exists(appRoot)) return appRoot;

        // 3. Dossier de données utilisateur
        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "streamlink.exe");
        if (File.Exists(dataBin)) return dataBin;

        // 4. Emplacement standard Streamlink (Program Files)
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progStreamlink = Path.Combine(programFiles, "Streamlink", "bin", "streamlink.exe");
        if (File.Exists(progStreamlink)) return progStreamlink;

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string progStreamlinkX86 = Path.Combine(programFilesX86, "Streamlink", "bin", "streamlink.exe");
        if (File.Exists(progStreamlinkX86)) return progStreamlinkX86;

        return "streamlink";
    }

    public static string GetFfmpegPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Dossier bin de l'application (embarqué dans la distribution)
        string appBin = Path.Combine(baseDir, "bin", "ffmpeg.exe");
        if (File.Exists(appBin)) return appBin;

        // 2. Racine de l'application
        string appRoot = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(appRoot)) return appRoot;

        // 3. Dossier de données utilisateur
        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "ffmpeg.exe");
        if (File.Exists(dataBin)) return dataBin;

        // 4. Emplacements standards Windows (%LOCALAPPDATA%\ffmpeg ou Program Files)
        string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(localApp)) return localApp;

        string progFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(progFiles)) return progFiles;

        return "ffmpeg";
    }

    public static string GetFfprobePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Dossier bin de l'application
        string appBin = Path.Combine(baseDir, "bin", "ffprobe.exe");
        if (File.Exists(appBin)) return appBin;

        // 2. Racine de l'application
        string appRoot = Path.Combine(baseDir, "ffprobe.exe");
        if (File.Exists(appRoot)) return appRoot;

        // 3. Dossier de données utilisateur
        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "ffprobe.exe");
        if (File.Exists(dataBin)) return dataBin;

        // 4. Emplacements standards Windows
        string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffprobe.exe");
        if (File.Exists(localApp)) return localApp;

        string progFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffprobe.exe");
        if (File.Exists(progFiles)) return progFiles;

        return "ffprobe";
    }

    public static string? GetNodePath()
    {
        string nvmNode = @"C:\nvm4w\nodejs\node.exe";
        if (File.Exists(nvmNode)) return nvmNode;

        string progFilesNode = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(progFilesNode)) return progFilesNode;

        string localAppNode = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "node", "node.exe");
        if (File.Exists(localAppNode)) return localAppNode;

        return null;
    }

    public static string GetPythonPath()
    {
        // 1. Lanceur standard py.exe de Windows
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pyLauncher = Path.Combine(winDir, "py.exe");
        if (File.Exists(pyLauncher)) return pyLauncher;

        // 2. Recherche générique dans LocalAppData\Programs\Python\Python*
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pythonRoot = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(pythonRoot))
        {
            var pyDirs = Directory.GetDirectories(pythonRoot, "Python*");
            Array.Sort(pyDirs, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(pyDirs); // Le plus récent en premier
            foreach (var dir in pyDirs)
            {
                string candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        // 3. Recherche dans Program Files
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progPyRoot = Path.Combine(progFiles, "Python");
        if (Directory.Exists(progPyRoot))
        {
            var pyDirs = Directory.GetDirectories(progPyRoot, "Python*");
            Array.Sort(pyDirs, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(pyDirs);
            foreach (var dir in pyDirs)
            {
                string candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return "python";
    }

    public static bool IsPythonInstalled()
    {
        string path = GetPythonPath();
        if (File.Exists(path)) return true;

        // Vérification si 'python' ou 'py' répond dans le PATH
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc != null)
            {
                bool exited = proc.WaitForExit(3000);
                return exited && proc.ExitCode == 0;
            }
        }
        catch
        {
            // Non disponible
        }

        return false;
    }

    public static string GetSnifferScriptPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string direct = Path.Combine(baseDir, "Scripts", "sniff_stream.py");
        if (File.Exists(direct)) return direct;

        string subCore = Path.Combine(baseDir, "src", "UberDownloader.Core", "Scripts", "sniff_stream.py");
        if (File.Exists(subCore)) return subCore;

        return direct;
    }
}
