using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SlapRift.League
{
    // libzstd.dll ships next to the plugin (BSD licence, Meta Platforms); it is loaded by full path once.
    internal static class Zstd
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string path);

        [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)]
        static extern UIntPtr ZSTD_decompress(byte[] dst, UIntPtr dstCapacity, byte[] src, UIntPtr srcSize);

        [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)]
        static extern uint ZSTD_isError(UIntPtr code);

        static bool loaded;

        public static void Load(string pluginDir)
        {
            if (loaded) return;
            var dll = Path.Combine(pluginDir, "libzstd.dll");
            if (LoadLibrary(dll) == IntPtr.Zero) throw new DllNotFoundException("cannot load " + dll);
            loaded = true;
        }

        public static byte[] Decompress(byte[] src, int srcLen, int size)
        {
            var input = src;
            if (srcLen != src.Length) { input = new byte[srcLen]; Buffer.BlockCopy(src, 0, input, 0, srcLen); }
            var dst = new byte[size];
            var r = ZSTD_decompress(dst, (UIntPtr)size, input, (UIntPtr)srcLen);
            if (ZSTD_isError(r) != 0) throw new InvalidDataException("zstd error");
            return dst;
        }
    }
}
