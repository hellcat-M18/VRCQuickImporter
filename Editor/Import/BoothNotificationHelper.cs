using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VRCQuickImporter.Editor.Import
{
    internal static class BoothNotificationHelper
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static NotifyIcon _activeNotifyIcon;

        public static void ShowNotification(string title, string message)
        {
            DisposeActiveIcon();

            var icon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipTitle = title,
                BalloonTipText = message,
                Visible = true
            };

            icon.BalloonTipClicked += (sender, args) =>
            {
                FocusUnityEditor();
                DisposeIcon(icon);
            };

            icon.BalloonTipClosed += (sender, args) => DisposeIcon(icon);

            _activeNotifyIcon = icon;
            icon.ShowBalloonTip(5000);
        }

        private static void FocusUnityEditor()
        {
            var mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (mainWindowHandle != IntPtr.Zero)
            {
                SetForegroundWindow(mainWindowHandle);
            }
        }

        private static void DisposeActiveIcon()
        {
            DisposeIcon(_activeNotifyIcon);
        }

        private static void DisposeIcon(NotifyIcon icon)
        {
            if (icon == null)
            {
                return;
            }

            try
            {
                icon.Visible = false;
                icon.Dispose();
            }
            catch
            {
                // 通知の後始末に失敗してもインポート処理へ影響させない。
            }

            if (ReferenceEquals(_activeNotifyIcon, icon))
            {
                _activeNotifyIcon = null;
            }
        }
    }
}
