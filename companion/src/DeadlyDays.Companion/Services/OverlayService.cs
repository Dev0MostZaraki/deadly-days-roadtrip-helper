using DeadlyDays.Companion.Models;
using DeadlyDays.Companion.Views;

namespace DeadlyDays.Companion.Services;

public sealed class OverlayService
{
    private readonly OverlayWindow _window = new();
    public bool Enabled { get; set; } = true;

    public void UpdateGameStatus(GameWindowSnapshot? game, string title, string detail = "")
    {
        if (!Enabled || game is null || !game.IsUsable)
        {
            _window.Hide();
            return;
        }
        _window.SetStatus(title, detail);
        _window.PositionNear(game);
        if (!_window.IsVisible) _window.Show();
    }

    public void Hide() => _window.Hide();
}
