extern alias EtpApplication;

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.13 (lane U1, wired at integration). Windows' own Authenticode check of a file (WinVerifyTrust, WINTRUST_ACTION_GENERIC_VERIFY_V2,
/// no UI, no revocation or network fetch: the ETP certificate is self-signed and has no CRL). Returns the state and, for a signed
/// file, the signer's SHA-1 thumbprint. It decides nothing: <see cref="InstallerAuthenticodeCheck"/> holds the rule.
/// </summary>
internal sealed class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int CertEUntrustedRoot = unchecked((int)0x800B0109);
    private const int CertEChaining = unchecked((int)0x800B010A);
    private const int CertEUntrustedTestRoot = unchecked((int)0x800B010D);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);

    public AuthenticodeInspection Inspect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException("The file to check was not found.", filePath);

        var result = Verify(filePath);
        var state = result switch
        {
            0 => AuthenticodeSignatureState.Valid,
            TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => AuthenticodeSignatureState.NotSigned,
            TrustEBadDigest => AuthenticodeSignatureState.Tampered,
            CertEUntrustedRoot or CertEChaining or CertEUntrustedTestRoot or TrustEExplicitDistrust or TrustESubjectNotTrusted => AuthenticodeSignatureState.NotTrusted,
            _ => AuthenticodeSignatureState.Unknown,
        };
        return new AuthenticodeInspection(state, state is AuthenticodeSignatureState.NotSigned ? null : SignerThumbprint(filePath));
    }

    private static int Verify(string filePath)
    {
        var path = Marshal.StringToHGlobalUni(filePath);
        var fileInfo = IntPtr.Zero;
        try
        {
            var info = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(), pcwszFilePath = path };
            fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(info, fileInfo, false);
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfo,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
            };
            var action = GenericVerifyV2;
            var noWindow = new IntPtr(-1);
            var result = WinVerifyTrust(noWindow, ref action, ref data);
            data.dwStateAction = WtdStateActionClose;
            _ = WinVerifyTrust(noWindow, ref action, ref data);
            return result;
        }
        finally
        {
            if (fileInfo != IntPtr.Zero) Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(path);
        }
    }

    private static string? SignerThumbprint(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // The signer of an Authenticode file; X509CertificateLoader has no equivalent for signed files.
            using var signer = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var certificate = new X509Certificate2(signer);
            return certificate.Thumbprint;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or Win32Exception or IOException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WintrustData pWVTData);
}
