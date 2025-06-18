using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Aviad
{
    internal static class LibraryLoader
    {
        static LibraryLoader()
        {
            Extension = ".dll";
        }

        public static string Extension { get; }

        public static IntPtr LoadLibrary(string path)
        {
            // Debug.Log($"[Aviad] LoadLibrary raw: {path}");
            var handle = Win32.LoadLibrary(path);
            if (handle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Debug.LogError($"[Aviad] LoadLibrary failed for '{path}'. Error: {errorCode}");
            }
            return handle;
        }

        public static T GetSymbolDelegate<T>(IntPtr library, string name) where T : Delegate
        {
            var symbol = Win32.GetProcAddress(library, name);
            if (symbol == IntPtr.Zero)
            {
                Debug.LogError($"[Aviad] Failed to load symbol '{name}' from library handle {library}");
                throw new EntryPointNotFoundException($"Unable to load symbol '{name}'.");
            }

            // Debug.Log($"[Aviad] Loaded symbol: {name}");
            return Marshal.GetDelegateForFunctionPointer<T>(symbol);
        }

        public static void FreeLibrary(IntPtr library)
        {
            if (library != IntPtr.Zero)
            {
                // Debug.Log($"[Aviad] Freeing library: {library}");
                Win32.FreeLibrary(library);
            }
        }

        private static class Win32
        {
            private const string Kernel32 = "kernel32.dll";

            [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Ansi)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Ansi)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

            [DllImport(Kernel32, SetLastError = true)]
            public static extern bool FreeLibrary(IntPtr hModule);
        }
    }
}
