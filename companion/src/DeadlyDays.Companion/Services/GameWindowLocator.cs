using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services;

public sealed class GameWindowLocator
{
    private static readonly string[] ProcessNames =
    {
        "DDSurvivors-Win64-Shipping",
        "DDSurvivors"
    };

    public GameWindowSnapshot? Find()
    {
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var hwnd = process.MainWindowHandle;
                    if (hwnd == 0) continue;
                    if (!TryGetClientBounds(hwnd, out var bounds)) continue;

                    return new GameWindowSnapshot(
                        hwnd,
                        process.Id,
                        process.ProcessName,
                        ReadWindowTitle(hwnd),
                        (int)bounds.X,
                        (int)bounds.Y,
                        (int)bounds.Width,
                        (int)bounds.Height,
                        IsIconic(hwnd));
                }
                catch
                {
                    // Process may terminate between enumeration and inspection.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return null;
    }

    private static string ReadWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool TryGetClientBounds(nint hwnd, out System.Windows.Rect rect)
    {
        rect = default;
        if (!GetClientRect(hwnd, out var client)) return false;
        var topLeft = new POINT { X = client.Left, Y = client.Top };
        var bottomRight = new POINT { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight)) return false;
        var width = Math.Max(0, bottomRight.X - topLeft.X);
        var height = Math.Max(0, bottomRight.Y - topLeft.Y);
        rect = new System.Windows.Rect(topLeft.X, topLeft.Y, width, height);
        return width > 0 && height > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);
}
