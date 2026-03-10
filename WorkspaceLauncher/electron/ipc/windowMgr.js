"use strict";

/**
 * Window management via PowerShell inline scripts.
 * Avoids native module compilation — works out-of-the-box on any Windows machine.
 */

const { execSync } = require("child_process");
const { ipcMain } = require("electron");

// ── PowerShell runner ──────────────────────────────────────────────────
function runPS(script) {
  try {
    return execSync(
      `powershell -NoProfile -NonInteractive -Command "${script.replace(/"/g, '\\"')}"`,
      {
        encoding: "utf8",
        timeout: 5000,
      },
    ).trim();
  } catch (err) {
    console.error("[WindowMgr] PS error:", err.message);
    return "";
  }
}

function runPSScript(lines) {
  const script = lines.join("\n");
  try {
    return execSync("powershell -NoProfile -NonInteractive -Command -", {
      input: script,
      encoding: "utf8",
      timeout: 8000,
    }).trim();
  } catch (err) {
    console.error("[WindowMgr] PS error:", err.message);
    return "";
  }
}

// ── Win32 type definitions (reused across calls) ───────────────────────
const WIN32_TYPES = `
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out RECT pvAttribute, uint cbSize);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
`;

/**
 * Snap a window HWND to a given rect (x, y, width, height).
 */
function snapWindow(hwnd, x, y, w, h) {
  runPSScript([
    WIN32_TYPES,
    `$hwnd = [IntPtr]${hwnd}`,
    `[Win32]::ShowWindow($hwnd, 9)`, // SW_RESTORE
    `[Win32]::SetWindowPos($hwnd, [IntPtr]::Zero, ${x}, ${y}, ${w}, ${h}, 0x0014)`,
  ]);
}

/**
 * Get current RECT for a window (DWM-accurate).
 */
function getWindowRect(hwnd) {
  const out = runPSScript([
    WIN32_TYPES,
    `$hwnd = [IntPtr]${hwnd}`,
    `$r = New-Object Win32+RECT`,
    `[Win32]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null`,
    `Write-Output "$($r.Left),$($r.Top),$($r.Right),$($r.Bottom)"`,
  ]);
  const parts = out.split(",").map(Number);
  if (parts.length !== 4) return null;
  return { left: parts[0], top: parts[1], right: parts[2], bottom: parts[3] };
}

/**
 * Get all visible windows with titles + process names.
 */
function listWindows() {
  const out = runPSScript([
    `Get-Process | Where-Object { $_.MainWindowTitle -ne '' } | ForEach-Object {`,
    `  $hwnd = $_.MainWindowHandle.ToInt64()`,
    `  $title = $_.MainWindowTitle`,
    `  $proc = $_.ProcessName`,
    `  Write-Output "$hwnd|$proc|$title"`,
    `}`,
  ]);

  if (!out) return [];

  return out
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const [hwnd, processName, ...titleParts] = line.split("|");
      return {
        hwnd: parseInt(hwnd, 10),
        processName: processName || "",
        title: titleParts.join("|") || "",
      };
    })
    .filter((w) => w.title && w.hwnd);
}

/**
 * Get virtual desktops from the Windows registry.
 * The Desktop IDs are stored under:
 *   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops
 * Desktop names are stored under:
 *   HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{GUID}
 */
function listDesktops() {
  const out = runPSScript([
    `$basePath = 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops\\Desktops'`,
    `$desktops = @()`,
    `if (Test-Path $basePath) {`,
    `  $keys = Get-ChildItem $basePath -ErrorAction SilentlyContinue`,
    `  $i = 1`,
    `  foreach ($key in $keys) {`,
    `    $name = (Get-ItemProperty -Path $key.PSPath -Name 'Name' -ErrorAction SilentlyContinue).Name`,
    `    $id = $key.PSChildName`,
    `    if (-not $name) { $name = "Escritorio $i" }`,
    `    Write-Output "$id|$name"`,
    `    $i++`,
    `  }`,
    `} else {`,
    `  Write-Output "1|Escritorio 1"`,
    `}`,
  ]);

  if (!out) return [{ id: "1", name: "Escritorio 1" }];

  return out
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const [id, ...nameParts] = line.split("|");
      return { id, name: nameParts.join("|") || `Escritorio` };
    });
}

/**
 * Bring a window to front.
 */
function bringToFront(hwnd) {
  runPSScript([
    WIN32_TYPES,
    `$hwnd = [IntPtr]${hwnd}`,
    `[Win32]::ShowWindow($hwnd, 9)`,
    `[Win32]::SetForegroundWindow($hwnd)`,
  ]);
}

// ── IPC Registration ───────────────────────────────────────────────────
function register(ipcMainModule, sendToRenderer) {
  // Fire-and-forget actions
  ipcMainModule.on("bridge:action", (event, msg) => {
    switch (msg.action) {
      case "snap_window":
        snapWindow(msg.hwnd, msg.x, msg.y, msg.w, msg.h);
        break;
      case "get_window_rect": {
        const rect = getWindowRect(msg.hwnd);
        sendToRenderer("window_rect", { hwnd: msg.hwnd, rect });
        break;
      }
      case "bring_to_front":
        bringToFront(msg.hwnd);
        break;
    }
  });

  // Request/response handlers
  ipcMainModule.handle("list-windows", async () => {
    try {
      return listWindows();
    } catch (err) {
      console.error("[WindowMgr] listWindows error:", err.message);
      return [];
    }
  });

  ipcMainModule.handle("list-desktops", async () => {
    try {
      return listDesktops();
    } catch (err) {
      console.error("[WindowMgr] listDesktops error:", err.message);
      return [{ id: "1", name: "Escritorio 1" }];
    }
  });
}

module.exports = {
  register,
  snapWindow,
  getWindowRect,
  listWindows,
  listDesktops,
  bringToFront,
};
