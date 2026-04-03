using System;
using System.Runtime.InteropServices;

namespace AppLavaluc.Services
{
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pDocName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pOutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        [DllImport("winspool.Drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool GetDefaultPrinter([Out] System.Text.StringBuilder pszBuffer, ref int pcchBuffer);

        public static bool TryGetDefaultPrinterName(out string? printerName)
        {
            printerName = null;
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
            try
            {
                var required = 0;
                GetDefaultPrinter(new System.Text.StringBuilder(0), ref required);
                var err = Marshal.GetLastWin32Error();
                if (required <= 0 || err == 0)
                {
                    return false;
                }

                var sb = new System.Text.StringBuilder(required);
                if (!GetDefaultPrinter(sb, ref required))
                {
                    return false;
                }

                var name = sb.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                printerName = name;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySendBytes(string printerName, byte[] bytes, out string? error)
        {
            error = null;
            if (!OperatingSystem.IsWindows())
            {
                error = "La impresión directa (ESC/POS RAW) solo está soportada en Windows.";
                return false;
            }
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pUnmanagedBytes = IntPtr.Zero;

            try
            {
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    error = "Nombre de impresora vacío.";
                    return false;
                }

                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    error = $"No se pudo abrir la impresora '{printerName}'. Win32Error={Marshal.GetLastWin32Error()}";
                    return false;
                }

                var di = new DOC_INFO_1
                {
                    pDocName = "Ticket",
                    pDataType = "RAW"
                };

                if (!StartDocPrinter(hPrinter, 1, di))
                {
                    error = $"No se pudo iniciar el documento. Win32Error={Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (!StartPagePrinter(hPrinter))
                {
                    error = $"No se pudo iniciar la página. Win32Error={Marshal.GetLastWin32Error()}";
                    return false;
                }

                pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out var dwWritten))
                {
                    error = $"Error al escribir en la impresora. Win32Error={Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (dwWritten != bytes.Length)
                {
                    error = "La impresora no recibió todos los bytes del ticket.";
                    return false;
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (pUnmanagedBytes != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pUnmanagedBytes);
                }

                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                }
            }
        }
    }
}
