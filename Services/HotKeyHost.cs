using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kaldir.Services;

// "Easy Uninstall (Ctrl+Alt+U)": kısayola basıldığında ön plandaki pencerenin programını bulup kaldırma akışına verir.
public static class HotKeyHost
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);

    static SubclassProc? _proc;   // GC'ye karşı canlı tut
    static Action? _callback;

    // Ctrl+Alt+U, başka bir program tutuyorsa Ctrl+Alt+K, sonra J denenir. Döndürülen metin etkin kısayoldur; null = hiçbiri alınamadı.
    public static string? Register(IntPtr hwnd, Action onHotKey)
    {
        _callback = onHotKey;
        _proc = (h, msg, w, l, id, data) =>
        {
            if (msg == WM_HOTKEY && w.ToInt32() == 1) { _callback?.Invoke(); return IntPtr.Zero; }
            return DefSubclassProc(h, msg, w, l);
        };
        if (!SetWindowSubclass(hwnd, _proc, (UIntPtr)1, UIntPtr.Zero)) return null;
        foreach (char key in "UKJ")
            if (RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key)) return $"Ctrl+Alt+{key}";
        return null;
    }

    public static void Unregister(IntPtr hwnd) { try { UnregisterHotKey(hwnd, 1); } catch { } }

    // Ön plandaki pencerenin exe yolu (Kaldir'ın kendisi hariç). Hotkey tuşa basıldığı anda odak hedef uygulamadadır.
    public static string? ForegroundExePath()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
