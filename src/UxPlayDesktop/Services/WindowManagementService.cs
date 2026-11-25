using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace UxPlayDesktop.Services;

public sealed class DisplayInfo : IEquatable<DisplayInfo>
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
    public int MonitorLeft { get; set; }
    public int MonitorTop { get; set; }
    public int MonitorRight { get; set; }
    public int MonitorBottom { get; set; }
    public int WorkLeft { get; set; }
    public int WorkTop { get; set; }
    public int WorkRight { get; set; }
    public int WorkBottom { get; set; }

    public string DisplayLabel
    {
        get
        {
            var isSpanish = LocalizationService.Instance.CurrentLanguage.StartsWith("es", StringComparison.OrdinalIgnoreCase);
            var primaryTag = IsPrimary ? (isSpanish ? " [Principal]" : " [Primary]") : "";
            return $"{Name} ({Width}x{Height}){primaryTag}";
        }
    }

    public bool Equals(DisplayInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Index == other.Index;
    }

    public override bool Equals(object? obj) => Equals(obj as DisplayInfo);

    public override int GetHashCode() => Index.GetHashCode();

    public override string ToString() => DisplayLabel;
}

public interface IWindowManagementService
{
    IReadOnlyList<DisplayInfo> GetConnectedDisplays();
    void SetAlwaysOnTop(bool enable, IntPtr? targetHwnd = null);
    string? CaptureScreenshot(string? outputDirectory = null);
    void MoveVideoWindowToDisplay(int displayIndex, bool forceFullscreen = false);

    /// <summary>Sizes the video window so its picture area is exactly width x height (one stream
    /// pixel per screen pixel, no scaling blur), optionally without borders, centered on the display.</summary>
    void FitVideoWindowPixelPerfect(int displayIndex, int width, int height, bool borderless);
}

public class WindowManagementService : IWindowManagementService
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SRCCOPY = 0x00CC0020;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_CAPTION = 0x00C00000L, WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L,
                       WS_MINIMIZEBOX = 0x00020000L, WS_MAXIMIZEBOX = 0x00010000L, WS_POPUP = 0x80000000L;
    private const long WS_EX_DLGMODALFRAME = 0x00000001L, WS_EX_CLIENTEDGE = 0x00000200L,
                       WS_EX_STATICEDGE = 0x00020000L, WS_EX_WINDOWEDGE = 0x00000100L;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public IReadOnlyList<DisplayInfo> GetConnectedDisplays()
    {
        var list = new List<DisplayInfo>();
        int index = 0;
        var isSpanish = LocalizationService.Instance.CurrentLanguage.StartsWith("es", StringComparison.OrdinalIgnoreCase);

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                int width = Math.Abs(mi.rcMonitor.Right - mi.rcMonitor.Left);
                int height = Math.Abs(mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                bool isPrimary = (mi.dwFlags & 1) != 0;

                list.Add(new DisplayInfo
                {
                    Index = index,
                    Name = isSpanish ? $"Pantalla {index + 1}" : $"Display {index + 1}",
                    DeviceName = mi.szDevice ?? string.Empty,
                    Width = width,
                    Height = height,
                    IsPrimary = isPrimary,
                    MonitorLeft = mi.rcMonitor.Left,
                    MonitorTop = mi.rcMonitor.Top,
                    MonitorRight = mi.rcMonitor.Right,
                    MonitorBottom = mi.rcMonitor.Bottom,
                    WorkLeft = mi.rcWork.Left,
                    WorkTop = mi.rcWork.Top,
                    WorkRight = mi.rcWork.Right,
                    WorkBottom = mi.rcWork.Bottom
                });
                index++;
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            list.Add(new DisplayInfo
            {
                Index = 0,
                Name = isSpanish ? "Pantalla Principal" : "Primary Display",
                Width = 1920,
                Height = 1080,
                IsPrimary = true,
                MonitorLeft = 0,
                MonitorTop = 0,
                MonitorRight = 1920,
                MonitorBottom = 1080,
                WorkLeft = 0,
                WorkTop = 0,
                WorkRight = 1920,
                WorkBottom = 1040
            });
        }

        return list;
    }

    public void SetAlwaysOnTop(bool enable, IntPtr? targetHwnd = null)
    {
        IntPtr hwnd = targetHwnd ?? FindAirPlayVideoHwnd();
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, enable ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    public void MoveVideoWindowToDisplay(int displayIndex, bool forceFullscreen = false)
    {
        IntPtr hwnd = FindAirPlayVideoHwnd();
        if (hwnd == IntPtr.Zero) return;

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        var displays = GetConnectedDisplays();
        if (displays.Count == 0) return;

        var targetDisplay = displays.FirstOrDefault(d => d.Index == displayIndex) ?? displays[0];

        if (forceFullscreen)
        {
            int targetX = targetDisplay.MonitorLeft;
            int targetY = targetDisplay.MonitorTop;
            int targetWidth = targetDisplay.Width;
            int targetHeight = targetDisplay.Height;

            SetWindowPos(hwnd, IntPtr.Zero, targetX, targetY, targetWidth, targetHeight, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }
        else
        {
            if (GetWindowRect(hwnd, out var curRect))
            {
                int curW = Math.Max(curRect.Right - curRect.Left, 640);
                int curH = Math.Max(curRect.Bottom - curRect.Top, 480);

                int workW = Math.Max(targetDisplay.WorkRight - targetDisplay.WorkLeft, targetDisplay.Width);
                int workH = Math.Max(targetDisplay.WorkBottom - targetDisplay.WorkTop, targetDisplay.Height);

                if (curW > workW) curW = workW;
                if (curH > workH) curH = workH;

                int targetX = targetDisplay.WorkLeft + Math.Max(0, (workW - curW) / 2);
                int targetY = targetDisplay.WorkTop + Math.Max(0, (workH - curH) / 2);

                SetWindowPos(hwnd, IntPtr.Zero, targetX, targetY, curW, curH, SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }
            else
            {
                int targetX = targetDisplay.WorkLeft + 50;
                int targetY = targetDisplay.WorkTop + 50;
                SetWindowPos(hwnd, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }
        }
    }

    public void FitVideoWindowPixelPerfect(int displayIndex, int width, int height, bool borderless)
    {
        if (width <= 0 || height <= 0) return;
        IntPtr hwnd = FindAirPlayVideoHwnd();
        if (hwnd == IntPtr.Zero) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        var displays = GetConnectedDisplays();
        if (displays.Count == 0) return;
        var target = displays.FirstOrDefault(d => d.Index == displayIndex) ?? displays[0];

        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if (borderless)
        {
            style = (style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP;
            exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE);
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }

        // Outer window size whose client (picture) area is exactly width x height.
        var rect = new RECT { Left = 0, Top = 0, Right = width, Bottom = height };
        AdjustWindowRectEx(ref rect, unchecked((uint)style), false, unchecked((uint)exStyle));
        int outerW = rect.Right - rect.Left;
        int outerH = rect.Bottom - rect.Top;

        // Center the picture area in the monitor's work area (taskbar excluded).
        int workW = target.WorkRight - target.WorkLeft;
        int workH = target.WorkBottom - target.WorkTop;
        int x = target.WorkLeft + (workW - width) / 2 + rect.Left;
        int y = target.WorkTop + Math.Max(0, (workH - height) / 2) + rect.Top;

        SetWindowPos(hwnd, IntPtr.Zero, x, y, outerW, outerH, SWP_SHOWWINDOW | SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public string? CaptureScreenshot(string? outputDirectory = null)
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AirPlay Desktop")
                : outputDirectory;

            Directory.CreateDirectory(dir);

            IntPtr targetHwnd = FindAirPlayVideoHwnd();
            if (targetHwnd == IntPtr.Zero) targetHwnd = GetDesktopWindow();

            GetWindowRect(targetHwnd, out var rect);
            int width = Math.Max(rect.Right - rect.Left, 640);
            int height = Math.Max(rect.Bottom - rect.Top, 480);

            IntPtr hdcSrc = GetWindowDC(targetHwnd);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            IntPtr hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);
            SelectObject(hdcDest, hOld);

            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            var filePath = Path.Combine(dir, $"AirPlay_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using (var fs = new FileStream(filePath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(fs);
            }

            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(targetHwnd, hdcSrc);

            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to capture screenshot: {ex.Message}");
            return null;
        }
    }

    public static IntPtr FindAirPlayVideoHwnd()
    {
        var uxplayPids = new HashSet<uint>();
        foreach (var proc in Process.GetProcessesByName("uxplay"))
        {
            try
            {
                uxplayPids.Add((uint)proc.Id);
            }
            catch { }
        }

        IntPtr foundHwnd = IntPtr.Zero;

        // Pass 1: Look for any visible window owned by an uxplay process
        if (uxplayPids.Count > 0)
        {
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (uxplayPids.Contains(pid))
                {
                    if (GetWindowRect(hWnd, out var rect) &&
                        (rect.Right - rect.Left > 10) &&
                        (rect.Bottom - rect.Top > 10))
                    {
                        foundHwnd = hWnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        if (foundHwnd != IntPtr.Zero) return foundHwnd;

        // Pass 2: Fallback by class name or window title
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, sbClass.Capacity);
            var className = sbClass.ToString();

            var sbTitle = new StringBuilder(256);
            GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
            var title = sbTitle.ToString();

            if (className.IndexOf("Gst", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("D3D11", StringComparison.OrdinalIgnoreCase) >= 0 ||
                className.IndexOf("D3D12", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("UxPlay", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (GetWindowRect(hWnd, out var rect) &&
                    (rect.Right - rect.Left > 10) &&
                    (rect.Bottom - rect.Top > 10))
                {
                    foundHwnd = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }
}
