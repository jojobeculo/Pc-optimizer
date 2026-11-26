using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FrameCore.Core.Platform;

public sealed record SignatureInfo(bool Valid, string? Subject, string? Error);

/// <summary>Authenticode verification (WinVerifyTrust) + SHA-256, used before running any external executable.</summary>
public static class Authenticode
{
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks;
        public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

    public static SignatureInfo Verify(string path)
    {
        if (!OperatingSystem.IsWindows()) return new(false, null, "not Windows");
        if (!File.Exists(path)) return new(false, null, "file not found");
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /* WTD_UI_NONE */, fdwRevocationChecks = 0,
                dwUnionChoice = 1 /* WTD_CHOICE_FILE */, pFile = pFile, dwStateAction = 0, dwProvFlags = 0x00000080 /* WTD_REVOCATION_CHECK_NONE... cache only */,
            };
            var rc = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            string? subject = null;
            try
            {
#pragma warning disable SYSLIB0057
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                subject = cert.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch { }
            return new(rc == 0, subject, rc == 0 ? null : $"0x{rc:X8}");
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    public static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }
}
