using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UsageViewer.Windows;

public static class CredentialCleanup
{
    // Codex rust-v0.167.0, login/src/auth/storage.rs: canonical CODEX_HOME -> cli|sha256[..16].
    public static string AccountKey(string profile) => "cli|"+ProfileHash(profile);
    // codex-rs/secrets/src/lib.rs: keyring service "codex", account secrets|sha256[..16].
    public static string SecretsAccountKey(string profile) => "secrets|"+ProfileHash(profile);

    private static string ProfileHash(string profile)
    {
        Directory.CreateDirectory(profile);
        using var directory=NativeMethods.CreateFile(profile,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path=new StringBuilder(32768);
        uint length=NativeMethods.GetFinalPathNameByHandle(directory,path,(uint)path.Capacity,0);
        if (length==0 || length>=path.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        string canonical=path.ToString().TrimEnd('\\');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()[..16];
    }

    public static void DeleteOwned(string profile)
    {
        string key=AccountKey(profile),secretsKey=SecretsAccountKey(profile);
        if (!NativeMethods.CredEnumerate(null,0,out uint count,out IntPtr entries))
        {
            if (Marshal.GetLastWin32Error()==1168)
            {DeleteEncryptedAccount(profile);return;}
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            for (int i=0;i<count;i++)
            {
                var item=Marshal.PtrToStructure<NativeMethods.Credential>(Marshal.ReadIntPtr(entries,i*IntPtr.Size));
                string target=Marshal.PtrToStringUni(item.TargetName)??"";
                // Never read CredentialBlob. Match only the pinned provider's service AND the exact profile key.
                bool legacy=target.Contains("Codex Auth",StringComparison.Ordinal)&&target.Contains(key,StringComparison.Ordinal);
                bool encrypted=target.Contains("codex",StringComparison.OrdinalIgnoreCase)&&target.Contains(secretsKey,StringComparison.Ordinal);
                if (item.Type!=1 || !(legacy||encrypted)) continue;
                if (!NativeMethods.CredDelete(target,item.Type,0) && Marshal.GetLastWin32Error()!=1168)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally { NativeMethods.CredFree(entries); }
        // Only after successful key cleanup. Never leave an undecryptable account file for a later install.
        DeleteEncryptedAccount(profile);
    }
    private static void DeleteEncryptedAccount(string profile)
    {
        try{File.Delete(Path.Combine(profile,"secrets","codex_auth.age"));}
        catch(DirectoryNotFoundException){/* A profile that never signed in has no encrypted directory. */}
    }
}
