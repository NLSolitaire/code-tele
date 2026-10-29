using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UberDownloader.Core.Services;

public class NativeMethods
{
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;
    public const int WM_HOTKEY = 0x0312;

    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}

public class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Global\\UberDownloader_Net10_SingleInstance_Mutex";
    private const string PipeName = "UberDownloader_Net10_IPC_Pipe";

    private Mutex? _mutex;
    private bool _isFirstInstance;
    private CancellationTokenSource? _pipeCts;

    public bool IsFirstInstance => _isFirstInstance;

    public event Action<string>? OnUrlOrMessageReceived;

    public bool CheckAndAcquire()
    {
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);
        if (!_isFirstInstance)
        {
            return false;
        }

        StartNamedPipeServer();
        return true;
    }

    public static async Task SendMessageToRunningInstanceAsync(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1500);
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await client.WriteAsync(bytes, 0, bytes.Length);
            await client.FlushAsync();
        }
        catch
        {
            // Silently fail if first instance is closing
        }
    }

    private void StartNamedPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);

                    using var ms = new MemoryStream();
                    byte[] buffer = new byte[1024];
                    int read;
                    while ((read = await server.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                    }

                    string message = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        OnUrlOrMessageReceived?.Invoke(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        if (_mutex != null)
        {
            if (_isFirstInstance)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ignore if not owned or already released
                }
                catch (ObjectDisposedException)
                {
                }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
