using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using ScpslApp;
using DmaBase.Misc;

namespace DmaBase
{
    public class MonitorInfoItem
    {
        public int Index { get; set; }
        public IntPtr Handle { get; set; }
        public string DeviceName { get; set; } = "";
        public string DisplayName => $"Monitor {Index + 1}{(IsPrimary ? " (Primary)" : "")}";
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; } = 144;
        public bool IsPrimary { get; set; }
    }

    public static class MonitorChooser
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEXW
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODEW
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODEW lpDevMode);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private static List<MonitorInfoItem> _cachedMonitors = new();
        private static MonitorInfoItem? _selectedMonitor;
        private static bool _needsSelection = false;
        private static long _lastExternalCheckTicks = 0;

        private static int _currentMonitorLeft = 0;
        private static int _currentMonitorTop = 0;
        private static int _currentMonitorWidth = 1920;
        private static int _currentMonitorHeight = 1080;
        private static int _currentMonitorRefresh = 144;

        /// <summary>
        /// True if running under External mode (auto-detects from compiler define or assembly name).
        /// </summary>
        public static bool IsExternalMode
        {
            get
            {
#if EXTERNAL
                return true;
#else
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "";
                return asm.Contains("External", StringComparison.OrdinalIgnoreCase);
#endif
            }
        }

        public static bool NeedsSelection => _needsSelection;
        public static List<MonitorInfoItem> Monitors => _cachedMonitors;
        public static string CurrentMonitorName => _selectedMonitor != null
            ? $"{_selectedMonitor.DisplayName} ({_selectedMonitor.Width}x{_selectedMonitor.Height} @ {_selectedMonitor.RefreshRate}Hz)"
            : "Primary Display";

        /// <summary>
        /// Scans and returns all active display monitors with exact native resolution and refresh rates.
        /// </summary>
        public static List<MonitorInfoItem> RefreshMonitors()
        {
            var list = new List<MonitorInfoItem>();
            int idx = 0;

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
                {
                    var mi = new MONITORINFOEXW();
                    mi.cbSize = Marshal.SizeOf<MONITORINFOEXW>();
                    if (GetMonitorInfoW(hMon, ref mi))
                    {
                        int refresh = 144;
                        try
                        {
                            var dm = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
                            if (EnumDisplaySettingsW(mi.szDevice, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 0)
                            {
                                refresh = dm.dmDisplayFrequency;
                            }
                        }
                        catch { }

                        bool isPrim = (mi.dwFlags & 1) != 0;
                        list.Add(new MonitorInfoItem
                        {
                            Index = idx++,
                            Handle = hMon,
                            DeviceName = mi.szDevice,
                            Left = mi.rcMonitor.Left,
                            Top = mi.rcMonitor.Top,
                            Width = mi.rcMonitor.Right - mi.rcMonitor.Left,
                            Height = mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                            RefreshRate = refresh,
                            IsPrimary = isPrim
                        });
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Monitor Enumeration Error] {ex.Message}");
            }

            if (list.Count == 0)
            {
                int defaultRefresh = 144;
                try
                {
                    var dm = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
                    if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 0)
                    {
                        defaultRefresh = dm.dmDisplayFrequency;
                    }
                }
                catch { }

                list.Add(new MonitorInfoItem
                {
                    Index = 0,
                    Handle = IntPtr.Zero,
                    DeviceName = "Primary",
                    Left = 0,
                    Top = 0,
                    Width = Math.Max(800, GetSystemMetrics(0)),
                    Height = Math.Max(600, GetSystemMetrics(1)),
                    RefreshRate = defaultRefresh,
                    IsPrimary = true
                });
            }

            _cachedMonitors = list;
            return list;
        }

        /// <summary>
        /// Locates the SCP: Secret Laboratory game window.
        /// </summary>
        public static IntPtr FindGameWindow()
        {
            IntPtr hwnd = FindWindow(null, "SCP: Secret Laboratory");
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                return hwnd;

            try
            {
                var procs = Process.GetProcessesByName("SCPSL");
                if (procs.Length == 0) procs = Process.GetProcessesByName("scpsl");

                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero && IsWindow(p.MainWindowHandle))
                        return p.MainWindowHandle;

                    IntPtr found = IntPtr.Zero;
                    uint pid = (uint)p.Id;
                    EnumWindows((h, l) =>
                    {
                        GetWindowThreadProcessId(h, out uint wPid);
                        if (wPid == pid && IsWindowVisible(h))
                        {
                            GetWindowRect(h, out RECT r);
                            if (r.Right - r.Left > 200 && r.Bottom - r.Top > 200)
                            {
                                found = h;
                                return false;
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (found != IntPtr.Zero)
                        return found;
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Returns the monitor bounds and refresh rate containing the game window.
        /// </summary>
        public static (int Left, int Top, int Width, int Height, int RefreshRate)? GetGameMonitorBounds()
        {
            IntPtr gameHwnd = FindGameWindow();
            if (gameHwnd == IntPtr.Zero)
                return null;

            IntPtr hMon = MonitorFromWindow(gameHwnd, MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero)
                return null;

            var mi = new MONITORINFOEXW();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEXW>();
            if (GetMonitorInfoW(hMon, ref mi))
            {
                int refresh = 144;
                try
                {
                    var dm = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
                    if (EnumDisplaySettingsW(mi.szDevice, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 0)
                    {
                        refresh = dm.dmDisplayFrequency;
                    }
                }
                catch { }

                int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                return (mi.rcMonitor.Left, mi.rcMonitor.Top, w, h, refresh);
            }

            return null;
        }

        /// <summary>
        /// Returns the initial metrics (Width, Height, MaxFps) for creating ScpslOverlay.
        /// In External mode, prioritizes the monitor hosting scpsl.exe.
        /// In DMA mode, defaults to the primary monitor.
        /// </summary>
        public static (int Width, int Height, int MaxFps) GetInitialMetrics()
        {
            RefreshMonitors();

            if (IsExternalMode)
            {
                var gameMon = GetGameMonitorBounds();
                if (gameMon.HasValue)
                {
                    return (gameMon.Value.Width, gameMon.Value.Height, gameMon.Value.RefreshRate);
                }
            }

            var prim = _cachedMonitors.Find(m => m.IsPrimary) ?? (_cachedMonitors.Count > 0 ? _cachedMonitors[0] : null);
            if (prim != null)
            {
                return (prim.Width, prim.Height, prim.RefreshRate);
            }

            return (1920, 1080, 144);
        }

        /// <summary>
        /// Called from ScpslOverlay.PostInitialized to set up initial monitor binding.
        /// </summary>
        public static void OnPostInitialized(GameReader.ScpslOverlay overlay)
        {
            RefreshMonitors();

            if (IsExternalMode)
            {
                _needsSelection = false;
                var gameMon = GetGameMonitorBounds();
                if (gameMon.HasValue)
                {
                    var g = gameMon.Value;
                    _currentMonitorLeft = g.Left;
                    _currentMonitorTop = g.Top;
                    _currentMonitorWidth = g.Width;
                    _currentMonitorHeight = g.Height;
                    _currentMonitorRefresh = g.RefreshRate;
                    overlay.UpdateBounds(g.Left, g.Top, g.Width, g.Height, g.RefreshRate);
                    Log.WriteLine($"[External Monitor] Bound overlay to game monitor: {g.Width}x{g.Height} @ {g.RefreshRate}Hz ({g.Left}, {g.Top})");
                }
                else
                {
                    var prim = _cachedMonitors.Find(m => m.IsPrimary) ?? _cachedMonitors[0];
                    _currentMonitorLeft = prim.Left;
                    _currentMonitorTop = prim.Top;
                    _currentMonitorWidth = prim.Width;
                    _currentMonitorHeight = prim.Height;
                    _currentMonitorRefresh = prim.RefreshRate;
                    overlay.UpdateBounds(prim.Left, prim.Top, prim.Width, prim.Height, prim.RefreshRate);
                    Log.WriteLine($"[External Monitor] Waiting for scpsl.exe window. Defaulted overlay to: {prim.Width}x{prim.Height} @ {prim.RefreshRate}Hz");
                }
            }
            else
            {
                // DMA Mode
                if (_cachedMonitors.Count <= 1)
                {
                    _needsSelection = false;
                    var mon = _cachedMonitors[0];
                    _selectedMonitor = mon;
                    _currentMonitorLeft = mon.Left;
                    _currentMonitorTop = mon.Top;
                    _currentMonitorWidth = mon.Width;
                    _currentMonitorHeight = mon.Height;
                    _currentMonitorRefresh = mon.RefreshRate;
                    overlay.UpdateBounds(mon.Left, mon.Top, mon.Width, mon.Height, mon.RefreshRate);
                }
                else
                {
                    // Multi-monitor applies: prompt user BEFORE main menu
                    _needsSelection = true;
                    var prim = _cachedMonitors.Find(m => m.IsPrimary) ?? _cachedMonitors[0];
                    _selectedMonitor = prim;
                    _currentMonitorLeft = prim.Left;
                    _currentMonitorTop = prim.Top;
                    _currentMonitorWidth = prim.Width;
                    _currentMonitorHeight = prim.Height;
                    _currentMonitorRefresh = prim.RefreshRate;
                    overlay.UpdateBounds(prim.Left, prim.Top, prim.Width, prim.Height, prim.RefreshRate);
                }
            }
        }

        /// <summary>
        /// Periodically called during Render() in External mode to track game window monitor changes.
        /// </summary>
        public static void UpdateExternal(GameReader.ScpslOverlay overlay)
        {
            if (!IsExternalMode) return;

            long now = Stopwatch.GetTimestamp();
            if (now - _lastExternalCheckTicks < Stopwatch.Frequency) // ~1s interval
                return;
            _lastExternalCheckTicks = now;

            var gameMon = GetGameMonitorBounds();
            if (gameMon.HasValue)
            {
                var g = gameMon.Value;
                if (g.Left != _currentMonitorLeft || g.Top != _currentMonitorTop ||
                    g.Width != _currentMonitorWidth || g.Height != _currentMonitorHeight ||
                    g.RefreshRate != _currentMonitorRefresh)
                {
                    _currentMonitorLeft = g.Left;
                    _currentMonitorTop = g.Top;
                    _currentMonitorWidth = g.Width;
                    _currentMonitorHeight = g.Height;
                    _currentMonitorRefresh = g.RefreshRate;
                    overlay.UpdateBounds(g.Left, g.Top, g.Width, g.Height, g.RefreshRate);
                    Log.WriteLine($"[External Monitor] Re-aligned overlay to game monitor: {g.Width}x{g.Height} @ {g.RefreshRate}Hz ({g.Left}, {g.Top})");
                }
            }
        }

        /// <summary>
        /// Renders the monitor selection dialog (DMA mode only, before the main menu).
        /// </summary>
        public static void RenderPicker(GameReader.ScpslOverlay overlay)
        {
            if (IsExternalMode || !_needsSelection) return;

            Vector2 displaySize = ImGui.GetIO().DisplaySize;
            float winWidth = 500f;
            float winHeight = 150f + (_cachedMonitors.Count * 70f);

            ImGui.SetNextWindowSize(new Vector2(winWidth, winHeight), ImGuiCond.Always);
            ImGui.SetNextWindowPos(new Vector2((displaySize.X - winWidth) * 0.5f, (displaySize.Y - winHeight) * 0.5f), ImGuiCond.Always);

            if (ImGui.Begin("Select Target Monitor##MonitorPicker", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.77f, 0.71f, 0.99f, 1.00f), "Multiple Displays Detected");
                ImGui.TextDisabled("Select which monitor SCP: Secret Laboratory is displayed on:");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                for (int i = 0; i < _cachedMonitors.Count; i++)
                {
                    var mon = _cachedMonitors[i];
                    ImGui.PushID(i);

                    string btnLabel = $"Use {mon.DisplayName} ({mon.Width}x{mon.Height} @ {mon.RefreshRate}Hz)##SelectMon";
                    if (ImGui.Button(btnLabel, new Vector2(winWidth - 32f, 44f)))
                    {
                        ApplyMonitor(overlay, mon);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Position: ({mon.Left}, {mon.Top})\nSize: {mon.Width}x{mon.Height}\nDevice: {mon.DeviceName}");
                    }

                    ImGui.Spacing();
                    ImGui.PopID();
                }

                ImGui.End();
            }
        }

        /// <summary>
        /// Applies the selected monitor, repositioning the overlay, updating target refresh rate, and closing the chooser.
        /// </summary>
        public static void ApplyMonitor(GameReader.ScpslOverlay overlay, MonitorInfoItem mon)
        {
            _selectedMonitor = mon;
            _currentMonitorLeft = mon.Left;
            _currentMonitorTop = mon.Top;
            _currentMonitorWidth = mon.Width;
            _currentMonitorHeight = mon.Height;
            _currentMonitorRefresh = mon.RefreshRate;
            _needsSelection = false;
            overlay.UpdateBounds(mon.Left, mon.Top, mon.Width, mon.Height, mon.RefreshRate);
            Log.WriteLine($"[Monitor Chooser] Applied {mon.DisplayName} ({mon.Width}x{mon.Height} @ {mon.RefreshRate}Hz)");
        }

        /// <summary>
        /// Allows the user to re-open the monitor selection screen from Settings (DMA mode only).
        /// </summary>
        public static void RequestSelection()
        {
            if (!IsExternalMode)
            {
                RefreshMonitors();
                if (_cachedMonitors.Count > 1)
                {
                    _needsSelection = true;
                }
            }
        }
    }
}
