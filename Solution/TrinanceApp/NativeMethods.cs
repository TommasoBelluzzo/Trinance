#region Using Directives
using System;
using System.Runtime.InteropServices;
using System.Text;
#endregion

namespace TrinanceApp
{
    internal static class NativeMethods
    {
        #region Imports
        [DllImport("Kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern Int32 GetShortPathName([MarshalAs(UnmanagedType.LPWStr)] String path, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, Int32 bufferSize);
        #endregion

        #region Methods
        public static String GetShortPath(String path)
        {
            try
            {
                Int32 bufferSize = 256;
                StringBuilder buffer = new StringBuilder(256);
                
                Int32 length = GetShortPathName(path, buffer, bufferSize);

                if (length == 0)
                    return path;

                return buffer.ToString();
            }
            catch
            {
                return path;
            }
        }
        #endregion
    }
}
