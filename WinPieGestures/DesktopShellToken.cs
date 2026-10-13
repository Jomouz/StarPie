using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinPieGestures;

/// <summary>降权前确认桌面 Shell 的令牌确实不是管理员／高完整性；探测失败时拒绝降权调用。</summary>
internal static class DesktopShellToken
{
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    internal delegate bool TokenInformationQuery(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);

    internal static bool IsStandardUser()
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) return false;
        GetWindowThreadProcessId(shell, out uint pid);
        using SafeProcessHandle process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out SafeAccessTokenHandle token)) return false;
        using (token)
            return IsStandardUser(token, GetTokenInformation);
    }

    // 可替换的只读查询接缝：回归测试不打开真实进程，也不触发启动或提权。
    internal static bool IsStandardUser(SafeAccessTokenHandle token, TokenInformationQuery query)
    {
        IntPtr integrityBuffer = IntPtr.Zero;
        IntPtr elevationBuffer = IntPtr.Zero;
        try
        {
            if (!TryQueryTokenInformation(token, 25, IntPtr.Size, query, out integrityBuffer)) return false; // TokenIntegrityLevel
            IntPtr sid = Marshal.ReadIntPtr(integrityBuffer);
            if (sid == IntPtr.Zero) return false;
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (count == 0) return false;
            int integrity = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            if (integrity >= 0x3000) return false; // HIGH or SYSTEM
            if (!TryQueryTokenInformation(token, 20, sizeof(int), query, out elevationBuffer)) return false; // TokenElevation
            return Marshal.ReadInt32(elevationBuffer) == 0;
        }
        finally
        {
            if (elevationBuffer != IntPtr.Zero) Marshal.FreeHGlobal(elevationBuffer);
            if (integrityBuffer != IntPtr.Zero) Marshal.FreeHGlobal(integrityBuffer);
        }
    }

    private static bool TryQueryTokenInformation(SafeAccessTokenHandle token, int informationClass,
        int minimumLength, TokenInformationQuery query, out IntPtr information)
    {
        information = IntPtr.Zero;
        // 不同信息类必须独立探测所需长度；TokenElevation 可能拒绝过大的缓冲区长度。
        // 空缓冲区探测返回失败是正常现象，使用系统给出的长度，随后再验证实际读取结果。
        query(token, informationClass, IntPtr.Zero, 0, out int length);
        if (length < minimumLength || length > 65536) return false;

        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!query(token, informationClass, buffer, length, out int returnedLength)
                || returnedLength < minimumLength || returnedLength > length)
                return false;
            information = buffer;
            buffer = IntPtr.Zero; // 成功后由调用方统一释放。
            return true;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }
}
