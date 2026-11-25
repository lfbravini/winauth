using System.Runtime.InteropServices;
using System.Security;

namespace WorkstationMfa.Core.Security;

public static class SecureMemory
{
    public static string SecureStringToString(SecureString secure)
    {
        ArgumentNullException.ThrowIfNull(secure);
        var ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }

    public static SecureString ToSecureString(string value)
    {
        var s = new SecureString();
        foreach (var c in value)
        {
            s.AppendChar(c);
        }
        s.MakeReadOnly();
        return s;
    }
}
