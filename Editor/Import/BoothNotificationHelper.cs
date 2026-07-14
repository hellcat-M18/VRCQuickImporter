using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRCQuickImporter.Editor.Import
{
    internal static class BoothNotificationHelper
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

        private const uint NIM_ADD = 0;
        private const uint NIM_MODIFY = 1;
        private const uint NIM_DELETE = 2;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_INFO = 0x10;
        private const uint NIIF_INFO = 1;
        private const uint NIIF_NONE = 0;

        private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);
        private static readonly IntPtr IDI_INFORMATION = new IntPtr(32516);

        // 通知を受け取る用のカスタムウィンドウメッセージ
        private const uint WM_TRAYICON = 0x400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
        }

        public static void ShowNotification(string title, string message)
        {
            var process = Process.GetCurrentProcess();
            var hwnd = process.MainWindowHandle;

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_INFO | NIF_ICON,
                hIcon = LoadIcon(IntPtr.Zero, IDI_INFORMATION),
                szInfoTitle = title,
                szInfo = message,
                uTimeoutOrVersion = 5000,    // 5秒
                dwInfoFlags = NIIF_INFO
            };

            // アイコン追加 + バルーン表示
            Shell_NotifyIcon(NIM_ADD, ref nid);

            // バルーンが閉じるまで少し待ってから削除
            // Shell_NotifyIconは非同期なので、アイコンは残るがバルーンは消える
            // 簡易的にタイマーで削除（別スレッドで待機）
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                System.Threading.Thread.Sleep(6000); // バルーン表示時間 + 余裕
                try
                {
                    Shell_NotifyIcon(NIM_DELETE, ref nid);
                }
                catch { }
            });

            // Unityを前面に
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }
        }
    }
}
