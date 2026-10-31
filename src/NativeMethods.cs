using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GuardianTerminal
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe int memcmp(byte* a, byte* b, UIntPtr count);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const int LVM_GETHEADER = 0x101F;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        // A text box's grey hint while it's empty (shown even when it has the focus)
        public static void SetCueBanner(IntPtr textBox, string hint)
        {
            SendMessage(textBox, 0x1501, (IntPtr)1, hint);  // EM_SETCUEBANNER
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);

        // Whether a process has a message box (or another standard dialog) open: a visible top-level window of the
        // dialog class, #32770.
        public static bool HasOpenDialog(int processId)
        {
            bool found = false;
            StringBuilder name = new StringBuilder(32);
            EnumWindows((h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != (uint)processId || !IsWindowVisible(h)) return true;
                name.Length = 0;
                GetClassName(h, name, name.Capacity);
                found = name.ToString() == "#32770";
                return !found;
            }, IntPtr.Zero);
            return found;
        }

        public static unsafe bool BytesEqual(byte[] a, int aOffset, byte[] b, int bOffset, int length)
        {
            if (length == 0) return true;
            fixed (byte* pa = a)
            fixed (byte* pb = b)
            {
                return memcmp(pa + aOffset, pb + bOffset, (UIntPtr)length) == 0;
            }
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            return a.Length == b.Length && BytesEqual(a, 0, b, 0, a.Length);
        }

        // Dark title bar on Windows 10 (1809+) and 11; ignored elsewhere.
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
