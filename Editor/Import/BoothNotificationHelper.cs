using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRCQuickImporter.Editor.Import
{
    internal static class BoothNotificationHelper
    {
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
        private static readonly IntPtr IDI_INFORMATION = new IntPtr(32516);
        private static readonly object NotificationLock = new object();
        private static bool _iconAdded;
        private static int _notificationGeneration;

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
#if UNITY_EDITOR_WIN
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

            int notificationGeneration;
            lock (NotificationLock)
            {
                var command = _iconAdded ? NIM_MODIFY : NIM_ADD;
                var shown = Shell_NotifyIcon(command, ref nid);
                if (!shown && command == NIM_MODIFY)
                {
                    // Explorer再起動などで既存アイコンが失われた場合は追加し直す。
                    shown = Shell_NotifyIcon(NIM_ADD, ref nid);
                }

                if (!shown)
                {
                    return;
                }

                _iconAdded = true;
                notificationGeneration = ++_notificationGeneration;
            }

            // 最終通知から一定時間後にアイコンを削除する。
            // 新しい通知が来た場合は世代番号が変わるため、古い削除タイマーは何もしない。
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                System.Threading.Thread.Sleep(6000); // バルーン表示時間 + 余裕
                lock (NotificationLock)
                {
                    if (!_iconAdded || notificationGeneration != _notificationGeneration)
                    {
                        return;
                    }

                    try
                    {
                        Shell_NotifyIcon(NIM_DELETE, ref nid);
                    }
                    catch { }
                    finally
                    {
                        _iconAdded = false;
                    }
                }
            });
#endif
        }

    }
}
