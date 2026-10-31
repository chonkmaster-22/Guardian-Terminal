using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GuardianTerminal.Build
{
    // Keeps a build's merges within a memory budget: each merge says how much it may hold (its files, decompressed)
    // and waits while the others already hold too much. One merge always runs, however big, so nothing waits forever.
    // With many mods changing the same big packs, this is what keeps a build from running the PC out of memory.
    internal sealed class MemoryGate
    {
        private readonly long budget;
        private long used;
        private int running;
        private readonly object sync = new object();

        public MemoryGate(long budget)
        {
            this.budget = budget;
        }

        public void Enter(long cost)
        {
            if (cost <= 0) return;
            lock (sync)
            {
                while (running > 0 && used + cost > budget) Monitor.Wait(sync);
                used += cost;
                running++;
            }
        }

        public void Leave(long cost)
        {
            if (cost <= 0) return;
            lock (sync)
            {
                used -= cost;
                running--;
                Monitor.PulseAll(sync);
            }
        }

        // A third of the PC's memory, between 1 and 6 GB
        public static long Budget()
        {
            long total = 8L << 30;
            try
            {
                MEMORYSTATUSEX m = new MEMORYSTATUSEX();
                m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref m)) total = (long)m.ullTotalPhys;
            }
            catch (Exception) { }
            return Math.Max(1L << 30, Math.Min(6L << 30, total / 3));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }
}
