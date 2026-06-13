using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Skua.Core.Utils;

public static class MemoryUtils
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

    /// <summary>
    /// Forces a massive Garbage Collection pass and trims the process working set, 
    /// significantly reducing the memory footprint of bloated COM objects like the Flash Player.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (UIntPtr)unchecked((uint)-1), (UIntPtr)unchecked((uint)-1));
        }
        catch 
        { 
            // Suppress errors (e.g. if run on non-Windows platforms or insufficient privileges)
        }
    }
}
