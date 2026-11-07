using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using DownloadsOSX.Interop;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// Authenticode check of update downloads. Any signature Windows trusts is accepted, so the certificate may change
    /// (renewal, another provider); so is the current build's own certificate (self-signed). An unsigned update, or one
    /// signed by an unknown untrusted certificate, needs the user's consent; a broken signature is rejected.
    /// Unsigned builds (dev, before signing was set up) skip the check.
    /// </summary>
    internal static class Authenticode
    {
        private enum SignatureState { Unsigned, Broken, Signed }

        private sealed class Signature
        {
            public SignatureState State;
            public X509Certificate2 Certificate;
            /// <summary>The chain ends in a root Windows trusts (false for a self-signed certificate).</summary>
            public bool Trusted;
        }

        /// <summary>
        /// True if the user should confirm installing <paramref name="downloaded"/>. Throws if its signature is broken
        /// (changed after signing, or a certificate Windows explicitly distrusts).
        /// </summary>
        public static bool UpdateNeedsConsent(string current, string downloaded)
        {
            var mine = Inspect(current);
            if (mine.State != SignatureState.Signed)
            {
                Log.Info("Current build isn't signed, skipping the update signature check");
                return false;
            }

            var theirs = Inspect(downloaded);
            switch (theirs.State)
            {
                case SignatureState.Broken:
                    throw new InvalidDataException(App.Str("UpdateSignatureInvalid"));
                case SignatureState.Unsigned:
                    Log.Warn("Update isn't signed, asking the user");
                    return true;
            }

            string signer = $"{theirs.Certificate.Subject} ({theirs.Certificate.Thumbprint})";
            if (theirs.Trusted || theirs.Certificate.Thumbprint == mine.Certificate.Thumbprint)
            {
                Log.Info($"Update signed by {signer}, trusted: {theirs.Trusted}");
                return false;
            }
            Log.Warn($"Update signed by an untrusted certificate {signer}, asking the user");
            return true;
        }

        private static Signature Inspect(string path)
        {
            int hr = Verify(path);
            if (hr == NativeMethods.TRUST_E_NOSIGNATURE) return new Signature { State = SignatureState.Unsigned };
            // These come after the signature and file hash have been checked: the file is intact,
            // only the certificate isn't trusted by Windows (self-signed) or has expired without a timestamp.
            bool intact = hr == 0 || hr == NativeMethods.CERT_E_UNTRUSTEDROOT || hr == NativeMethods.CERT_E_CHAINING || hr == NativeMethods.CERT_E_EXPIRED;
            if (!intact)
            {
                Log.Warn($"Signature of {path} is invalid: 0x{hr:X8}");
                return new Signature { State = SignatureState.Broken };
            }
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return new Signature { State = SignatureState.Signed, Certificate = cert, Trusted = hr == 0 };
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the signer certificate of " + path, ex);
                return new Signature { State = SignatureState.Broken };
            }
        }

        private static int Verify(string path)
        {
            var file = new NativeMethods.WINTRUST_FILE_INFO
            {
                cbStruct = Marshal.SizeOf(typeof(NativeMethods.WINTRUST_FILE_INFO)),
                pcwszFilePath = path,
            };
            IntPtr pFile = Marshal.AllocCoTaskMem(file.cbStruct);
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new NativeMethods.WINTRUST_DATA
                {
                    cbStruct = Marshal.SizeOf(typeof(NativeMethods.WINTRUST_DATA)),
                    dwUIChoice = NativeMethods.WTD_UI_NONE,
                    fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                    dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = NativeMethods.WTD_STATEACTION_IGNORE,
                };
                return NativeMethods.WinVerifyTrust(new IntPtr(-1), NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data);
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(NativeMethods.WINTRUST_FILE_INFO));
                Marshal.FreeCoTaskMem(pFile);
            }
        }
    }
}
