using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// A stand-in for savagewind.exe in the loading screen tests (LaunchProbe): writes the log lines Savage Wind writes as
// it starts (savagewind.log next to the exe, the last one kept as savagewind.prev.log), timed like a real start, then
// does what SWMM_FAKE_MODE says:
//   ok      frames: the game is on screen
//   fatal   a FATAL line, then it exits with 1
//   dialog  a FATAL line and a message box of its own (a dialog-class window, off screen) for 4 s, then exits with 1
//   crash   exits with 0xC0000005 before its first frame
//   setup   its own setup screen ([launcher])
internal static class FakeGame
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private static StreamWriter log;

    private static int Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(dir, "savagewind.log"), prev = Path.Combine(dir, "savagewind.prev.log");
        if (File.Exists(path))
        {
            if (File.Exists(prev)) File.Delete(prev);
            File.Move(path, prev);
        }
        log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        string mode = Environment.GetEnvironmentVariable("SWMM_FAKE_MODE") ?? "ok";
        Line("[savagewind] Savage Wind 0.0.0-fake (" + mode + ")");
        Line("[paths] data: " + dir.TrimEnd('\\'));
        if (mode == "setup")
        {
            Wait(0.5);
            Line("[launcher] game files: setting up");
            Wait(8);
            return 0;
        }
        Wait(0.8);
        Line("[gamelib] extracted/swgame.dll (game version 1.9.0, 62 sites)");
        Wait(1.2);
        Line("[window] fullscreen 1280x720 (borderless)");
        Line("[vk] swapchain 1280x720, 4 images, present mode fifo relaxed");
        Wait(1.5);
        Line("[fs] romfs image extracted/romfs: 14431 MB, laid out in 4.53 s");
        if (Environment.GetEnvironmentVariable("SAVAGEWIND_ROMFS_OVERLAY") != null) Line("[fs] overlay: 629 romfs files replaced");
        Line("[fs] romfs index: 41062 files");
        Wait(1.2);
        if (mode == "fatal")
        {
            Line("FATAL: guest memory: the test's made-up error");
            return 1;
        }
        if (mode == "crash")
        {
            Wait(0.3);
            return unchecked((int)0xC0000005);
        }
        if (mode == "dialog")
        {
            Line("FATAL: guest memory: the test's made-up error");
            IntPtr box = CreateWindowEx(0, "#32770", "Savage Wind", 0x10000000 | unchecked((int)0x80000000), -3000, -3000, 300, 120,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);  // (WS_VISIBLE | WS_POPUP)
            Line("[test] message box window " + box);
            Wait(4);
            DestroyWindow(box);
            return 1;
        }
        Line("[aoc] ListAddOnContent(0, 2): 2 of 2");
        Line("[fs] romfs image extracted/aoc1: 1325 MB, laid out in 0.73 s");
        Line("[fs] add-on content 1: extracted/aoc1");
        Wait(1.5);
        Line("[vk] t=9.7 frame 123 60.9 fps max 25ms (1928 draws, 1 skipped)");
        Wait(6);
        return 0;
    }

    private static void Line(string text)
    {
        log.Write(text + "\n");
    }

    private static void Wait(double seconds)
    {
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
    }
}
