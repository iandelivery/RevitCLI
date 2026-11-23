param([int]$Target = 0)
if ($Target -le 0) { Write-Output 'usage: dialog-sweep.ps1 -Target <revit-pid>'; exit 1 }
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class Win32Sweep {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static List<IntPtr> Dialogs(uint wantPid) {
        var list = new List<IntPtr>();
        EnumWindows((h, lp) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == wantPid && IsWindowVisible(h)) {
                var c = new StringBuilder(256); GetClassNameW(h, c, 256);
                if (c.ToString() == "#32770") list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string Title(IntPtr h) {
        var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
        return t.ToString();
    }

    public static List<KeyValuePair<string, IntPtr>> Buttons(IntPtr parent) {
        var list = new List<KeyValuePair<string, IntPtr>>();
        EnumChildWindows(parent, (h, lp) => {
            var c = new StringBuilder(256); GetClassNameW(h, c, 256);
            if (c.ToString() == "Button") {
                var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
                list.Add(new KeyValuePair<string, IntPtr>(t.ToString(), h));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

for ($i = 0; $i -lt 15; $i++) {
    $dlgs = [Win32Sweep]::Dialogs($Target)
    if ($dlgs.Count -eq 0) { "round $i : no dialogs"; break }
    foreach ($d in $dlgs) {
        $title = [Win32Sweep]::Title($d)
        $btns = [Win32Sweep]::Buttons($d)
        $want = if ($title -match 'Unsigned') { 'Load Once' } else { 'Close' }
        $btn = $btns | Where-Object { $_.Key -replace '&','' -match [regex]::Escape($want) } | Select-Object -First 1
        if ($btn) {
            "round $i : '$title' -> clicking '$($btn.Key)'"
            [Win32Sweep]::PostMessageW($btn.Value, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        } else {
            "round $i : '$title' buttons: $($btns.Key -join ' | ')"
        }
    }
    Start-Sleep -Seconds 3
}
"remaining dialogs: $([Win32Sweep]::Dialogs($Target).Count)"
