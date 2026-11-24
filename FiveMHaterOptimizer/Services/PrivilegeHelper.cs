using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FiveMHaterOptimizer.Services;

public static class PrivilegeHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Geçerli işlem belirtecinde (token) bir ayrıcalığı etkinleştirir (SeDebugPrivilege, SeIncreaseQuotaPrivilege...).</summary>
    public static bool EnablePrivilege(string privilegeName, out string error)
    {
        error = string.Empty;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out token))
            {
                error = $"OpenProcessToken hata {Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
            {
                error = $"LookupPrivilegeValue hata {Marshal.GetLastWin32Error()}";
                return false;
            }

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
            };

            if (!NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                error = $"AdjustTokenPrivileges hata {Marshal.GetLastWin32Error()}";
                return false;
            }

            int last = Marshal.GetLastWin32Error();
            if (last == NativeMethods.ERROR_NOT_ALL_ASSIGNED)
            {
                error = "Ayrıcalık bu kullanıcı belirtecine atanmamış (ERROR_NOT_ALL_ASSIGNED)";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) NativeMethods.CloseHandle(token);
        }
    }
}
