using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DScanner.DirectInput;

public sealed class CooperativeWindowHandle : IDisposable
{
    private const uint WmQuit = 0x0012;

    private readonly ManualResetEventSlim _initialized = new();
    private readonly Thread? _windowThread;
    private Exception? _initializationException;
    private uint _windowThreadId;
    private bool _ownsWindow;
    private bool _disposed;

    public CooperativeWindowHandle()
    {
        Handle = GetConsoleWindow();
        if (Handle != nint.Zero)
        {
            return;
        }

        _windowThread = new Thread(RunWindowThread)
        {
            IsBackground = true,
            Name = "DScanner-DirectInput-Window"
        };
        _windowThread.Start();
        _initialized.Wait();

        if (_initializationException is not null)
        {
            throw new InvalidOperationException(
                "Could not create a process-owned window for DirectInput.",
                _initializationException);
        }
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsWindow && _windowThread is not null)
        {
            PostThreadMessage(_windowThreadId, WmQuit, nint.Zero, nint.Zero);
            _windowThread.Join(TimeSpan.FromSeconds(5));
        }

        _initialized.Dispose();
    }

    private void RunWindowThread()
    {
        try
        {
            _windowThreadId = GetCurrentThreadId();
            Handle = CreateWindowEx(
                0,
                "STATIC",
                "DScanner DirectInput",
                0,
                0,
                0,
                0,
                0,
                nint.Zero,
                nint.Zero,
                GetModuleHandle(null),
                nint.Zero);

            if (Handle == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _ownsWindow = true;
        }
        catch (Win32Exception exception)
        {
            _initializationException = exception;
        }
        finally
        {
            _initialized.Set();
        }

        if (!_ownsWindow)
        {
            return;
        }

        while (GetMessage(out Message message, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(in message);
            DispatchMessage(in message);
        }

        DestroyWindow(Handle);
        Handle = nint.Zero;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out Message message,
        nint window,
        uint messageFilterMinimum,
        uint messageFilterMaximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in Message message);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
