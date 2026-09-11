using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;
    private const uint WdaExcludeFromCapture = 0x00000011;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ConfigureNativeWindow();
    }

    public void SetStatus(string title, string detail = "")
    {
        TitleText.Text = title;
        DetailText.Text = detail;
    }

    public void PositionNear(GameWindowSnapshot game)
    {
        if (!game.IsUsable) return;
        Left = game.X + game.Width - Width - 24;
        Top = game.Y + 24;
    }

    private void ConfigureNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        _ = SetWindowLongPtr(hwnd, GwlExstyle, new nint(ex | WsExTransparent | WsExToolwindow | WsExNoactivate));
        _ = SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
}
