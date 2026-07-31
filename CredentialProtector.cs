using System.Runtime.InteropServices;
using System.Text;

namespace Snapshot;

internal static class CredentialProtector
{
    private const uint CryptprotectUiForbidden = 0x1;

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var inputBytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectBytes(inputBytes));
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(protectedValue);
            return Encoding.UTF8.GetString(UnprotectBytes(encrypted));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] ProtectBytes(byte[] input)
    {
        var inputBlob = CreateBlob(input);
        try
        {
            if (!CryptProtectData(
                    ref inputBlob,
                    "Snapshot Translator key",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptprotectUiForbidden,
                    out var outputBlob))
            {
                throw new InvalidOperationException(
                    "无法使用 Windows 数据保护功能保存翻译密钥。",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }

            return ReadAndFreeBlob(outputBlob);
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputBlob.Data);
            }
        }
    }

    private static byte[] UnprotectBytes(byte[] input)
    {
        var inputBlob = CreateBlob(input);
        try
        {
            if (!CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptprotectUiForbidden,
                    out var outputBlob))
            {
                throw new InvalidOperationException(
                    "无法读取已加密的翻译密钥。",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }

            return ReadAndFreeBlob(outputBlob);
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputBlob.Data);
            }
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        var blob = new DataBlob
        {
            Size = bytes.Length,
            Data = Marshal.AllocHGlobal(bytes.Length)
        };
        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    private static byte[] ReadAndFreeBlob(DataBlob blob)
    {
        try
        {
            var result = new byte[blob.Size];
            if (blob.Size > 0)
            {
                Marshal.Copy(blob.Data, result, 0, blob.Size);
            }

            return result;
        }
        finally
        {
            if (blob.Data != IntPtr.Zero)
            {
                LocalFree(blob.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
