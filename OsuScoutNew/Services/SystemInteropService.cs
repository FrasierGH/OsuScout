using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using OsuScoutNew.Core;

namespace OsuScoutNew.Services
{
    public enum FocusResult
    {
        Focused,
        // osu! isn't running.
        NotRunning,
        // osu! is running but this desktop won't let another app bring it to the front
        // (Wayland, or X11 without xdotool).
        CouldNotFocus
    }

    // The few things that work differently on each operating system.
    public static class SystemInteropService
    {
        private const int SW_RESTORE = 9;

        // --- WINDOWS P/INVOKES ---
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // --- ENCAPSULATED LOGIC ---

        // A dark title bar to match the app (Windows 10 2004 and later; older builds used
        // attribute 19 for the same thing, and anything older keeps a light one). Other
        // systems draw the title bar from the desktop's own theme.
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }

        // Brings a window to the front (Windows). A game in fullscreen minimises itself when it
        // loses focus (lazer: MinimiseOnFocusLossInFullscreen), so it is restored first, but only
        // then: restoring a window that isn't minimised can knock it out of fullscreen.
        public static bool FocusWindow(IntPtr handle)
        {
            if (!OperatingSystem.IsWindows()) return false;
            if (handle == IntPtr.Zero || !IsWindow(handle)) return false;
            if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
            return SetForegroundWindow(handle);
        }

        public static FocusResult FocusOsuProcess(OsuClient client, IEnumerable<string> processNames)
        {
            try
            {
                using var game = GameClients.FindRunningGame(client, processNames);
                if (game == null) return FocusResult.NotRunning;

                if (OperatingSystem.IsWindows())
                    return FocusWindow(game.MainWindowHandle) ? FocusResult.Focused : FocusResult.CouldNotFocus;

                return FocusX11Window(game.Id) ? FocusResult.Focused : FocusResult.CouldNotFocus;
            }
            catch
            {
                // e.g. access denied while inspecting the process
                return FocusResult.CouldNotFocus;
            }
        }

        // X11 lets one app raise another's window, but .NET has no API for it; xdotool does it
        // when it's installed. Wayland doesn't allow it at all (and lazer's SDL window is native
        // Wayland there), so on Wayland this finds nothing and the user switches themselves.
        private static bool FocusX11Window(int pid)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
            try
            {
                using var xdotool = Process.Start(new ProcessStartInfo("xdotool")
                {
                    ArgumentList = { "search", "--onlyvisible", "--pid", pid.ToString(), "windowactivate" },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (xdotool == null) return false;
                return xdotool.WaitForExit(3000) && xdotool.ExitCode == 0;
            }
            catch
            {
                // xdotool isn't installed
                return false;
            }
        }

        // Opens the system file manager showing this file (Windows: selected in Explorer;
        // elsewhere the folder holding it).
        public static void ShowInFileManager(string filePath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", new[] { "-R", filePath });
                else
                    Process.Start("xdg-open", new[] { Path.GetDirectoryName(filePath) });
            }
            catch
            {
                // No file manager to open; the dialog already said where the file is.
            }
        }
    }
}
