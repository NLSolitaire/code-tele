using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace UberDownloaderLauncher
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Console.Title = "Über-Downloader";
            }
            catch { }

            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpperInvariant();
            string msgUpdating = "⏳ Mise à jour & compilation incrémentale...";
            string msgSuccess = "✓ Compilation prête. Lancement...";
            string msgFailed = "❌ Erreur de compilation.";
            string msgNotFound = "❌ Fichier introuvable :";
            string msgPressKey = "Appuyez sur une touche pour quitter...";

            if (lang == "ES")
            {
                msgUpdating = "⏳ Comprobando actualizaciones y compilando...";
                msgSuccess = "✓ Compilación lista. Iniciando...";
                msgFailed = "❌ Error de compilación.";
                msgNotFound = "❌ Archivo no encontrado:";
                msgPressKey = "Presione cualquier tecla para salir...";
            }
            else if (lang == "EN")
            {
                msgUpdating = "⏳ Checking updates & compiling...";
                msgSuccess = "✓ Build ready. Starting...";
                msgFailed = "❌ Build failed.";
                msgNotFound = "❌ File not found:";
                msgPressKey = "Press any key to exit...";
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================");
            Console.WriteLine("              Über-Downloader");
            Console.WriteLine("==================================================");
            Console.ResetColor();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = FindProjectRoot(baseDir);

            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
            {
                string fallback = @"c:\Users\HP\Downloads\code-tele\Über-Downloader";
                if (Directory.Exists(fallback))
                {
                    projectRoot = fallback;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(msgNotFound + " " + baseDir);
                    Console.ResetColor();
                    Console.WriteLine(msgPressKey);
                    Console.ReadKey();
                    return;
                }
            }

            string slnxPath = Path.Combine(projectRoot, "UberDownloader.slnx");
            string wpfExePath = Path.Combine(projectRoot, "src", "UberDownloader.Wpf", "bin", "Debug", "net10.0-windows", "UberDownloader.Wpf.exe");

            try
            {
                var existingProcs = Process.GetProcessesByName("UberDownloader.Wpf");
                foreach (var p in existingProcs)
                {
                    try
                    {
                        p.CloseMainWindow();
                        if (!p.WaitForExit(1500))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msgUpdating);
            Console.ResetColor();

            try
            {
                var buildPsi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = string.Format("build \"{0}\" -c Debug --nologo -v q", slnxPath),
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                using (var buildProc = Process.Start(buildPsi))
                {
                    if (buildProc != null)
                    {
                        buildProc.WaitForExit();
                        if (buildProc.ExitCode != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(msgFailed);
                            Console.ResetColor();
                            Console.WriteLine(msgPressKey);
                            Console.ReadKey();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("{0} ({1})", msgFailed, ex.Message));
                Console.ResetColor();
                Console.WriteLine(msgPressKey);
                Console.ReadKey();
                return;
            }

            if (!File.Exists(wpfExePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(msgNotFound + " " + wpfExePath);
                Console.ResetColor();
                Console.WriteLine(msgPressKey);
                Console.ReadKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msgSuccess);
            Console.ResetColor();

            try
            {
                string passArgs = (args != null && args.Length > 0) ? string.Join(" ", args) : string.Empty;
                var appPsi = new ProcessStartInfo
                {
                    FileName = wpfExePath,
                    Arguments = passArgs,
                    WorkingDirectory = Path.GetDirectoryName(wpfExePath),
                    UseShellExecute = true
                };

                Process.Start(appPsi);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("Erreur : {0}", ex.Message));
                Console.ResetColor();
                Console.WriteLine(msgPressKey);
                Console.ReadKey();
            }
        }

        private static string FindProjectRoot(string startDir)
        {
            string current = startDir;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "UberDownloader.slnx")))
                {
                    return current;
                }

                string subProject = Path.Combine(current, "Über-Downloader");
                if (File.Exists(Path.Combine(subProject, "UberDownloader.slnx")))
                {
                    return subProject;
                }

                var parent = Directory.GetParent(current);
                current = (parent != null) ? parent.FullName : null;
            }

            return null;
        }
    }
}
