"use strict";

/**
 * Global keyboard/mouse hooks.
 *
 * Keyboard shortcuts: via Electron's globalShortcut API.
 * X1/X2 mouse buttons: via a background PowerShell watcher that uses
 * WH_MOUSE_LL (low-level mouse hook) and reports button events via stdout.
 *
 * When X1/X2 are pressed, we switch virtual desktops by simulating
 * Win+Ctrl+Left/Right keyboard shortcuts via SendKeys.
 */

const { globalShortcut, app } = require("electron");
const { spawn, execSync } = require("child_process");

let psHookProcess = null;
let sendFn = null;

// ── Virtual Desktop Switching ──────────────────────────────────────────
function switchDesktop(direction) {
  try {
    const key = direction === "next" ? "{RIGHT}" : "{LEFT}";
    // Use VBS/SendKeys to simulate Win+Ctrl+Arrow (avoids COM interop issues)
    execSync(
      `powershell -NoProfile -NonInteractive -Command "` +
        `Add-Type -AssemblyName System.Windows.Forms; ` +
        `[System.Windows.Forms.SendKeys]::SendWait('^(%{${key === "{RIGHT}" ? "RIGHT" : "LEFT"}})'); "` +
        `"`,
      { timeout: 3000, stdio: "ignore" },
    );
  } catch {
    // Fallback: use nircmd or direct keyboard simulation
    try {
      const keyCombo =
        direction === "next"
          ? '$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys("^#{RIGHT}")'
          : '$wsh = New-Object -ComObject WScript.Shell; $wsh.SendKeys("^#{LEFT}")';
      execSync(
        `powershell -NoProfile -NonInteractive -Command "${keyCombo.replace(/"/g, '\\"')}"`,
        {
          timeout: 3000,
          stdio: "ignore",
        },
      );
    } catch (err) {
      console.error("[Hooks] Desktop switch error:", err.message);
    }
  }
}

// ── Keyboard shortcuts via Electron globalShortcut ─────────────────────
function registerKeyboardShortcuts(hotkeys, sendToRenderer) {
  globalShortcut.unregisterAll();

  const map = {
    [hotkeys.cycle_forward]: () =>
      sendToRenderer("hotkey", { action: "cycle_forward" }),
    [hotkeys.cycle_backward]: () =>
      sendToRenderer("hotkey", { action: "cycle_backward" }),
    [hotkeys.util_reload_layouts]: () =>
      sendToRenderer("hotkey", { action: "reload_layouts" }),
  };

  for (const [combo, handler] of Object.entries(map)) {
    if (
      !combo ||
      combo.startsWith("_") ||
      combo.includes("x1") ||
      combo.includes("x2") ||
      combo.includes("mouse")
    )
      continue;
    try {
      // Convert "ctrl+alt+pagedown" to Electron "Ctrl+Alt+PageDown"
      const electronCombo = combo
        .split("+")
        .map((k) => k.charAt(0).toUpperCase() + k.slice(1))
        .join("+");
      const ok = globalShortcut.register(electronCombo, handler);
      if (!ok)
        console.warn(`[Hooks] Could not register shortcut: ${electronCombo}`);
      else console.log(`[Hooks] Registered shortcut: ${electronCombo}`);
    } catch (err) {
      console.warn(`[Hooks] Shortcut error for '${combo}':`, err.message);
    }
  }
}

// ── X1/X2 mouse button watcher via PowerShell ─────────────────────────
// Spawns a background PowerShell script that watches for X button events
// and sends messages back via stdout (line-delimited).
const PS_HOOK_SCRIPT = `
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class MouseHook {
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelMouseProc _proc = null;
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT {
        public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo;
    }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage([In] ref MSG lpMsg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

    static Action<string> _callback;
    public static void Start(Action<string> callback) {
        _callback = callback;
        _proc = HookProc;
        _hook = SetWindowsHookEx(14, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero) {
            Console.Error.WriteLine("ERROR: SetWindowsHookEx failed");
            return;
        }
        Console.Error.WriteLine("HOOK_READY");
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0)) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }
    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        // WM_XBUTTONDOWN = 0x020B
        if (nCode == 0 && (uint)wParam == 0x020B) {
            var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int btn = (int)((data.mouseData >> 16) & 0xFFFF);
            bool alt   = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool ctrl  = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            string prefix = "";
            if (ctrl)  prefix += "ctrl+";
            if (alt)   prefix += "alt+";
            if (shift) prefix += "shift+";
            string btnName = btn == 1 ? "x1" : "x2";
            _callback(prefix + btnName);
            return (IntPtr)1; // Consume the event
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
'@
[MouseHook]::Start({ param($e) [Console]::WriteLine($e); [Console]::Out.Flush() })
`;

function startMouseHookWatcher(sendToRenderer, hotkeys) {
  if (psHookProcess) return;

  try {
    psHookProcess = spawn(
      "powershell",
      ["-NoProfile", "-NonInteractive", "-Command", PS_HOOK_SCRIPT],
      {
        stdio: ["ignore", "pipe", "pipe"],
      },
    );

    // Log stderr for debugging
    psHookProcess.stderr.on("data", (data) => {
      console.log("[Hooks/PS]", data.toString().trim());
    });

    psHookProcess.stdout.on("data", (data) => {
      const lines = data
        .toString()
        .split("\n")
        .map((l) => l.trim())
        .filter(Boolean);
      for (const line of lines) {
        console.log("[Hooks] Mouse event:", line);
        handleMouseEvent(line, sendToRenderer, hotkeys);
      }
    });

    psHookProcess.on("exit", (code) => {
      console.log("[Hooks] Mouse hook process exited with code:", code);
      psHookProcess = null;
    });

    console.log(
      "[Hooks] Mouse hook watcher started (PID:",
      psHookProcess.pid,
      ")",
    );
  } catch (err) {
    console.warn("[Hooks] Could not start mouse hook watcher:", err.message);
  }
}

/**
 * Handle mouse events from the PowerShell hook.
 * Maps button presses to their configured actions.
 */
function handleMouseEvent(eventStr, sendToRenderer, hotkeys) {
  if (!hotkeys) return;

  // Default mappings: x1 = desktop forward, x2 = desktop backward
  // alt+x1 = zone cycle forward, alt+x2 = zone cycle backward
  const desktopFwd = hotkeys.desktop_cycle_fwd || "x1";
  const desktopBwd = hotkeys.desktop_cycle_bwd || "x2";
  const zoneFwd = hotkeys.mouse_cycle_fwd || "alt+x1";
  const zoneBwd = hotkeys.mouse_cycle_bwd || "alt+x2";

  const desktopEnabled = hotkeys._desktop_cycle_enabled !== false;
  const zoneEnabled = hotkeys._zone_cycle_enabled !== false;

  // Match event to configured hotkey
  if (desktopEnabled && eventStr === desktopFwd) {
    console.log("[Hooks] → Switching desktop NEXT");
    switchDesktop("next");
  } else if (desktopEnabled && eventStr === desktopBwd) {
    console.log("[Hooks] → Switching desktop PREV");
    switchDesktop("prev");
  } else if (zoneEnabled && eventStr === zoneFwd) {
    console.log("[Hooks] → Zone cycle forward");
    sendToRenderer("hotkey", { action: "cycle_forward" });
  } else if (zoneEnabled && eventStr === zoneBwd) {
    console.log("[Hooks] → Zone cycle backward");
    sendToRenderer("hotkey", { action: "cycle_backward" });
  }
}

function cleanup() {
  globalShortcut.unregisterAll();
  if (psHookProcess) {
    psHookProcess.kill();
    psHookProcess = null;
  }
}

// ── IPC Registration ───────────────────────────────────────────────────
function register(ipcMain, sendToRenderer) {
  sendFn = sendToRenderer;

  ipcMain.on("bridge:action", (event, msg) => {
    if (msg.action === "get_state") {
      const configHandler = require("./config.js");
      const config = configHandler.register._getConfig?.();
      if (config?.hotkeys) {
        registerKeyboardShortcuts(config.hotkeys, sendToRenderer);
        startMouseHookWatcher(sendToRenderer, config.hotkeys);
      }
    }

    if (msg.action === "save_config" && msg.hotkeys) {
      // Re-register shortcuts when config changes
      registerKeyboardShortcuts(msg.hotkeys, sendToRenderer);
      // Restart mouse hook with new hotkeys
      if (psHookProcess) {
        psHookProcess.kill();
        psHookProcess = null;
      }
      startMouseHookWatcher(sendToRenderer, msg.hotkeys);
    }
  });

  app.on("ready", () => {
    const configHandler = require("./config.js");
    const config = configHandler.register._getConfig?.();
    if (config?.hotkeys) {
      startMouseHookWatcher(sendToRenderer, config.hotkeys);
    }
  });
}

module.exports = { register, cleanup };
