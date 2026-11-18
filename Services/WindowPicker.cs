using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Kaldir.Models;

namespace Kaldir.Services;

// Easy Uninstall seçicisi: (1) açık üst düzey pencerelerin listesi, (2) ekranda bir pencereye tıklayarak seçim.
public static class WindowPicker
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public POINT pt; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KBLL { public uint vk, scan, flags, time; public IntPtr extra; }

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern long GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint f, StringBuilder s, ref uint n);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cur, uint id);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint win);

    const int GWL_EXSTYLE = -20; const long WS_EX_TOOLWINDOW = 0x80;
    const uint GW_OWNER = 4, GA_ROOT = 2;

    // Kendi kendini kaldırmaya değmeyecek Windows süreçleri
    static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    { "explorer.exe", "ApplicationFrameHost.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe", "TextInputHost.exe", "SystemSettings.exe", "LockApp.exe" };

    public static string? ExeOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); uint n = (uint)sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
        finally { CloseHandle(h); }
    }

    public static bool IsShell(string exe) => Shell.Contains(Path.GetFileName(exe));

    // Görev çubuğunda görünen pencereler: görünür, sahipsiz, araç penceresi değil, gizlenmemiş (cloaked), başlıklı.
    public static List<WindowItem> ListWindows()
    {
        var res = new List<WindowItem>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            if ((GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);
            string? exe = ExeOf(h);
            if (exe == null || IsShell(exe)) return true;
            res.Add(new WindowItem { Handle = h, Title = sb.ToString(), ExePath = exe });
            return true;
        }, IntPtr.Zero);
        return res;
    }

    // Bir sonraki sol tıklamanın altındaki pencerenin exe'si. Tıklama hedefe gitmez (yutulur).
    // Sağ tık veya Esc iptal eder; süre dolarsa null. Kanca UI iş parçacığında kurulur (WinUI mesaj döngüsü pompalar).
    public static Task<string?> PickByClickAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<string?>();
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        IntPtr mouseHook = IntPtr.Zero, keyHook = IntPtr.Zero;
        bool swallowUp = false;
        HookProc? mouse = null, key = null;

        void Finish(string? r)
        {
            if (tcs.Task.IsCompleted) return;
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
            RestoreCursor();
            GC.KeepAlive(mouse); GC.KeepAlive(key);
            tcs.TrySetResult(r);
        }

        mouse = (code, w, l) =>
        {
            if (code >= 0)
            {
                int msg = w.ToInt32();
                if (msg == 0x0201) // WM_LBUTTONDOWN
                {
                    var d = Marshal.PtrToStructure<MSLL>(l);
                    IntPtr top = GetAncestor(WindowFromPoint(d.pt), GA_ROOT);
                    swallowUp = true;
                    string? exe = top != IntPtr.Zero ? ExeOf(top) : null;
                    // Kancayı mesaj işlenirken değil hemen sonra kaldır
                    dq?.TryEnqueue(() => Finish(exe ?? ""));
                    return (IntPtr)1;
                }
                if (msg == 0x0202 && swallowUp) { swallowUp = false; return (IntPtr)1; }   // WM_LBUTTONUP
                if (msg == 0x0204) // WM_RBUTTONDOWN = iptal
                {
                    dq?.TryEnqueue(() => Finish(null));
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(mouseHook, code, w, l);
        };
        key = (code, w, l) =>
        {
            if (code >= 0 && w.ToInt32() == 0x0100 && Marshal.PtrToStructure<KBLL>(l).vk == 0x1B)   // Esc
                dq?.TryEnqueue(() => Finish(null));
            return CallNextHookEx(keyHook, code, w, l);
        };

        IntPtr mod = GetModuleHandle(null);
        mouseHook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, mouse, mod, 0);
        keyHook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, key, mod, 0);
        if (mouseHook == IntPtr.Zero) { Finish(null); return tcs.Task; }
        SetCrossCursor();
        _ = Task.Delay(timeout).ContinueWith(_ => dq?.TryEnqueue(() => Finish(null)));
        return tcs.Task;
    }

    // Seçim sırasında imleç artı işareti; sonra sistem imleçleri geri yüklenir.
    static void SetCrossCursor()
    {
        try
        {
            IntPtr cross = LoadCursor(IntPtr.Zero, 32515 /* IDC_CROSS */);
            SetSystemCursor(CopyIcon(cross), 32512 /* OCR_NORMAL */);
            SetSystemCursor(CopyIcon(cross), 32649 /* OCR_HAND */);
            SetSystemCursor(CopyIcon(cross), 32513 /* OCR_IBEAM */);
        }
        catch { }
    }
    public static void RestoreCursor() { try { SystemParametersInfo(0x0057 /* SPI_SETCURSORS */, 0, IntPtr.Zero, 0); } catch { } }
}
