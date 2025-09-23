// The MIT License (MIT)
//
// Copyright (c) 2025 belnytheraseiche
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.Json;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
#if DEBUG
using System.Reflection;
#else
using System.Diagnostics;
#endif

namespace KbdBounceGuard;

partial class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var m = new Mutex(true, "kbdbounceguard", out var createdNew);
        try
        {
            if (!createdNew)
                MessageBox(0, "already running.", "info", MB_OK | MB_ICONINFORMATION);
            else
            {
#if DEBUG
                var baseDirectory = Environment.CurrentDirectory;
#else
                var baseDirectory = AppContext.BaseDirectory;
#endif
                using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDirectory, "config.json")));
                var hookSettings = new HookSettings()
                {
                    ChatterThreshold = config.RootElement.GetProperty("chatter_threshold").GetInt32(),
                    RepeatThreshold = config.RootElement.GetProperty("repeat_threshold").GetInt32(),
                    UpDownThreshold = config.RootElement.GetProperty("updown_threshold").GetInt32(),
                    IgnoreRepeat = config.RootElement.GetProperty("ignore_repeat").GetBoolean(),
                    KeyUpChatter = config.RootElement.GetProperty("keyup_chatter").GetBoolean(),
                    AllowImeCtrlBackspace = config.RootElement.GetProperty("allow_ime_ctrl_backspace").GetBoolean(),
                };

                unsafe
                {
                    var succeeded = StartHook(&hookSettings);
                    if (!succeeded)
                        throw new Exception("could not start hook.");
                }

                try
                {
                    using var app = new TrayApplication();
                    app.Run();
                }
                finally
                {
                    EndHook();
                }
            }
        }
        catch (Exception exception)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), @"kbdbounceguard.log"), $"{DateTime.Now:F}: {exception}{Environment.NewLine}-{Environment.NewLine}");
            MessageBox(0, "(>_<)", "fatal", MB_OK | MB_ICONERROR);
        }
        finally
        {
            m.ReleaseMutex();
        }
    }

    // 
    // 

    [StructLayout(LayoutKind.Sequential)]
    struct HookSettings
    {
        public int ChatterThreshold;
        public int RepeatThreshold;
        public int UpDownThreshold;
        [MarshalAs(UnmanagedType.Bool)] public bool IgnoreRepeat;
        [MarshalAs(UnmanagedType.Bool)] public bool KeyUpChatter;
        [MarshalAs(UnmanagedType.Bool)] public bool AllowImeCtrlBackspace;
    }

    [LibraryImport("kbdhook.dll", EntryPoint = "StartHook")]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool StartHook(HookSettings* hookSettings);

    [LibraryImport("kbdhook.dll", EntryPoint = "EndHook")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial void EndHook();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string lpText, string? lpCaption, uint uType);

    const uint MB_OK = 0x00000000u;
    const uint MB_ICONINFORMATION = 0x00000040u;
    const uint MB_ICONERROR = 0x00000010u;

    class AutoReleaseHGlobalPointer(nint pointer) : IDisposable
    {
        public nint Pointer { get; } = pointer;

        public void Dispose()
        => Marshal.FreeHGlobal(this.Pointer);

        public static implicit operator nint(AutoReleaseHGlobalPointer obj) => obj.Pointer;
    }

    partial class TrayApplication : IDisposable
    {
        readonly nint windowHandle_;
        readonly GCHandle windowProcedureHandle_;
        readonly nint iconHandle_;

        // 
        // 

        public TrayApplication()
        {
            var hInstance = GetModuleHandle(null);
            iconHandle_ = GetIconHandle();
            try
            {
                using var namePointer = new AutoReleaseHGlobalPointer(Marshal.StringToHGlobalUni(WindowClassName));

                var windowProcedure = new WndProc(WindowProcedure);
                windowProcedureHandle_ = GCHandle.Alloc(windowProcedure);
                var wc = new WNDCLASSEX()
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(windowProcedure),
                    hInstance = hInstance,
                    hIcon = iconHandle_,
                    hIconSm = iconHandle_,
                    lpszClassName = namePointer,
                };

                unsafe
                {
                    if (RegisterClassEx(&wc) == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                windowHandle_ = CreateWindowEx(0, WindowClassName, "", 0, Int16.MinValue, Int16.MinValue, 0, 0, HWND_MESSAGE, 0, hInstance, 0);
                if (windowHandle_ == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var iconData = new NOTIFYICONDATA()
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = windowHandle_,
                    uID = 1,
                    uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
                    uCallbackMessage = WM_APP_NOTIFYICON,
                    hIcon = iconHandle_,
                };
                "KBD BOUNCE GUARD".AsSpan().CopyTo(iconData.szTip);
                unsafe
                {
                    if (!Shell_NotifyIcon(NIM_ADD, &iconData))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Win32Exception)
            {
                if (windowHandle_ != 0)
                    DestroyWindow(windowHandle_);
                else
                {
                    if (iconHandle_ != 0)
                        DestroyIcon(iconHandle_);
                    if (windowProcedureHandle_.IsAllocated)
                        windowProcedureHandle_.Free();
                }
                throw;
            }
        }

        public void Dispose()
        {
        }

        public void Run()
        {
            while (GetMessage(out var msg, 0, 0, 0) != 0)
            {
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
        }

        nint WindowProcedure(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            switch (msg)
            {
                case WM_APP_NOTIFYICON:
                    if (lParam == WM_RBUTTONUP)
                        ShowContextMenu();
                    break;
                case WM_COMMAND:
                    if (wParam == IDM_EXIT)
                        DestroyWindow(windowHandle_);
                    break;
                case WM_QUERYENDSESSION:
                    DestroyWindow(windowHandle_);
                    return 1;
                case WM_DESTROY:
                    {
                        var iconData = new NOTIFYICONDATA()
                        {
                            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                            hWnd = windowHandle_,
                            uID = 1,
                        };
                        unsafe
                        {
                            Shell_NotifyIcon(NIM_DELETE, &iconData);
                        }
                        DestroyIcon(iconHandle_);
                        windowProcedureHandle_.Free();
                        PostQuitMessage(0);
                    }
                    break;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        void ShowContextMenu()
        {
            var menu = CreatePopupMenu();
            AppendMenu(menu, MF_STRING, IDM_EXIT, "&Quit");
            GetCursorPos(out var point);
            SetForegroundWindow(windowHandle_);
            TrackPopupMenu(menu, 0, point.X, point.Y, 0, windowHandle_, 0);
            PostMessage(windowHandle_, WM_NULL, 0, 0);
            DestroyMenu(menu);
        }

        static nint GetIconHandle()
        {
#if DEBUG
            var file = Assembly.GetExecutingAssembly().Location;
#else
            var file = Process.GetCurrentProcess().MainModule?.FileName;
#endif
            if (file == null)
                return 0;

            unsafe
            {
                nint icon = 0;
                if (1 != ExtractIconEx(file, 0, null, &icon, 1))
                    return 0;
                else
                    return icon;
            }
        }

        // 
        // 

        const string WindowClassName = "KBDBOUNCEGUARDWINDOW";
        const nint HWND_MESSAGE = -3;
        const uint WM_APP = 0x8000u;
        const uint WM_APP_NOTIFYICON = WM_APP + 1u;
        const uint WM_DESTROY = 0x0002u;
        const uint WM_RBUTTONUP = 0x0205u;
        const uint WM_COMMAND = 0x0111u;
        const uint WM_QUERYENDSESSION = 0x0011u;
        const uint WM_NULL = 0x0000u;
        const uint NIM_ADD = 0x00u;
        const uint NIM_DELETE = 0x02u;
        const uint NIF_MESSAGE = 0x01u;
        const uint NIF_ICON = 0x02u;
        const uint NIF_TIP = 0x04u;
        const uint MF_STRING = 0x00u;
        const uint IDM_EXIT = 1001u;

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public nint lpszMenuName;
            public nint lpszClassName;
            public nint hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [InlineArray(64)]
        struct String64
        {
            char element_;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [InlineArray(128)]
        struct String128
        {
            char element_;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [InlineArray(256)]
        struct String256
        {
            char element_;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONDATA
        {
            public uint cbSize;
            public nint hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public nint hIcon;
            public String128 szTip;
            public uint dwState;
            public uint dwStateMask;
            public String256 szTipInfo;
            public uint uTimeoutOrVersion;
            public String64 szInfoTitle;
            public uint dwInfoFlags;
        }

        delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        private static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool TranslateMessage(in MSG lpMsg);

        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static partial nint DispatchMessage(in MSG lpMsg);

        [LibraryImport("user32.dll")]
        private static partial void PostQuitMessage(int nExitCode);

        [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
        private static unsafe partial ushort RegisterClassEx(WNDCLASSEX* lpwcx);

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int X, int Y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static partial nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hWnd);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint GetModuleHandle(string? lpModuleName);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint CreatePopupMenu();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyMenu(nint hMenu);

        [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AppendMenu(nint hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint hIcon);

        [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static unsafe partial uint ExtractIconEx(string lpszFile, int nIconIndex, nint* phiconLarge, nint* phiconSmall, uint nIcons);

        [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool Shell_NotifyIcon(uint dwMessage, NOTIFYICONDATA* lpData);
    }
}
