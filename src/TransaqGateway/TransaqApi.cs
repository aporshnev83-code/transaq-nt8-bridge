using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TransaqGateway
{
    public class TransaqApi
    {
        private readonly object _callLock = new object();
        private CallbackDelegate _callback;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool CallbackDelegate(IntPtr msg);

        [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr SendCommand([MarshalAs(UnmanagedType.LPStr)] string xml);

        [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SetCallback(CallbackDelegate callback);

        [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool FreeMemory(IntPtr ptr);

        public void Initialize(Func<string, bool> onMessage)
        {
            _callback = delegate (IntPtr ptr)
            {
                var msg = ReadUtf8Z(ptr);
                if (ptr != IntPtr.Zero)
                {
                    FreeMemory(ptr);
                }
                return onMessage(msg);
            };

            lock (_callLock)
            {
                SetCallback(_callback);
            }
        }

        public string Send(string xml)
        {
            lock (_callLock)
            {
                var ptr = SendCommand(xml);
                var response = ReadUtf8Z(ptr);
                if (ptr != IntPtr.Zero)
                {
                    FreeMemory(ptr);
                }
                return response;
            }
        }

        private static string ReadUtf8Z(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            var len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
            {
                len++;
            }

            if (len == 0)
            {
                return string.Empty;
            }

            var buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }
    }
}
