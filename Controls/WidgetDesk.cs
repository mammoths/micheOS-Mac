using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Miche.Mac.Models;

namespace Miche.Mac.Controls;

// Logical coordinates within the scrollable desk; screen pixels are never used here.
public sealed class WidgetDesk : Panel
{
    private WidgetPlacement? _placement;
    private Func<WidgetPlacement, bool>? _save;
    private Gesture? _gesture;
    private Rect _card;
    private double _deskWidth;
    private const double MinCardWidth = 320, MinCardHeight = 220, MaxTop = 10000;
    private sealed record Gesture(WidgetPlacement Before, Rect Card, Point Start, IPointer Pointer, bool Resize)
    { public bool Started { get; set; } }
    public WidgetDesk()
    {
        Focusable = true;
        ClipToBounds = true;
        AddHandler(KeyDownEvent, DeskKeyDown, RoutingStrategies.Tunnel);
    }
    public void Configure(WidgetPlacement placement, Func<WidgetPlacement, bool> save)
    {
        if (_gesture is not null && _placement?.WidgetInstanceId != placement.WidgetInstanceId) CancelGesture();
        _save = save;
        if (_gesture is null) _placement = Copy(placement);
        InvalidateMeasure();
    }
    private static WidgetPlacement Copy(WidgetPlacement p) => new() {
        WidgetInstanceId = p.WidgetInstanceId, Mode = p.Mode, PosX = p.PosX, PosY = p.PosY,
        WidthFraction = p.WidthFraction, Height = p.Height, Window = p.Window, BeforeRemoval = p.BeforeRemoval
    };
    private static double Snap(double value) => Math.Round(value / 8) * 8;
    private Rect CardAt(double deskWidth)
    {
        if (_placement is null) return default;
        var minimum = Math.Min(MinCardWidth, deskWidth);
        var width = Math.Clamp(_placement.WidthFraction * deskWidth, minimum, deskWidth);
        // First placement remains centered like the source preview.
        if (_placement.PosX < 0) width = Math.Min(width, 540);
        var left = _placement.PosX < 0 ? (deskWidth - width) / 2 : _placement.PosX * deskWidth;
        return new Rect(Math.Clamp(left, 0, Math.Max(0, deskWidth - width)),
            Math.Clamp(_placement.PosY < 0 ? 0 : _placement.PosY, 0, MaxTop), width,
            Math.Clamp(_placement.Height, MinCardHeight, 3000));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Max(1, double.IsFinite(availableSize.Width) ? availableSize.Width : 900);
        var card = CardAt(width);
        foreach (var child in Children) child.Measure(card.Size);
        return new Size(width, Math.Max(MinCardHeight, card.Bottom + 24));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_gesture is not null && Math.Abs(_deskWidth - finalSize.Width) > .5) CancelGesture();
        _deskWidth = Math.Max(1, finalSize.Width);
        _card = CardAt(_deskWidth);
        foreach (var child in Children) child.Arrange(_card);
        return finalSize;
    }
    public void BeginGesture(PointerPressedEventArgs e, bool resize)
    {
        if (_placement is null || _gesture is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _gesture = new Gesture(Copy(_placement), _card, e.GetPosition(this), e.Pointer, resize);
        Focus(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gesture is not { } gesture || _placement is null) return;
        var delta = e.GetPosition(this) - gesture.Start;
        if (!gesture.Started && Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
        gesture.Started = true;
        var card = gesture.Card;
        if (gesture.Resize)
        {
            var width = Math.Clamp(Snap(card.Width + delta.X), Math.Min(MinCardWidth, _deskWidth), Math.Max(Math.Min(MinCardWidth, _deskWidth), _deskWidth - card.X));
            var height = Math.Clamp(Snap(card.Height + delta.Y), MinCardHeight, 3000);
            SetPreview(new Rect(card.X, card.Y, width, height));
        }
        else
        {
            var left = Math.Clamp(Snap(card.X + delta.X), 0, Math.Max(0, _deskWidth - card.Width));
            var top = Math.Clamp(Snap(card.Y + delta.Y), 0, MaxTop);
            SetPreview(new Rect(left, top, card.Width, card.Height));
        }
        e.Handled = true;
    }
    private void SetPreview(Rect card)
    {
        if (_placement is null) return;
        _placement.PosX = card.X / _deskWidth; _placement.PosY = card.Y;
        _placement.WidthFraction = card.Width / _deskWidth; _placement.Height = card.Height;
        InvalidateMeasure();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_gesture is not { } gesture) return;
        _gesture = null;
        gesture.Pointer.Capture(null);
        if (gesture.Started && _placement is not null)
        {
            if (_save?.Invoke(Copy(_placement)) != true) _placement = gesture.Before;
        }
        else _placement = gesture.Before;
        InvalidateMeasure(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { base.OnPointerCaptureLost(e); CancelGesture(); }
    public bool CancelGesture()
    {
        if (_gesture is not { } gesture) return false;
        _gesture = null; _placement = gesture.Before;
        gesture.Pointer.Capture(null); InvalidateMeasure(); return true;
    }
    private void DeskKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CancelGesture()) { e.Handled = true; return; }
        if (!ReferenceEquals(e.Source, this) || _gesture is not null || _placement is null ||
            e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        if (e.KeyModifiers == KeyModifiers.Alt) KeyboardChange(e, resize: false);
        else if (e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift)) KeyboardChange(e, resize: true);
    }
    private void KeyboardChange(KeyEventArgs e, bool resize)
    {
        var before = Copy(_placement!);
        var dx = e.Key == Key.Left ? -8 : e.Key == Key.Right ? 8 : 0;
        var dy = e.Key == Key.Up ? -8 : e.Key == Key.Down ? 8 : 0;
        var card = _card;
        var changed = resize
            ? new Rect(card.X, card.Y, Math.Clamp(card.Width + dx, Math.Min(MinCardWidth, _deskWidth), Math.Max(Math.Min(MinCardWidth, _deskWidth), _deskWidth - card.X)), Math.Clamp(card.Height + dy, MinCardHeight, 3000))
            : new Rect(Math.Clamp(card.X + dx, 0, Math.Max(0, _deskWidth - card.Width)), Math.Clamp(card.Y + dy, 0, MaxTop), card.Width, card.Height);
        SetPreview(changed);
        if (_save?.Invoke(Copy(_placement!)) != true) { _placement = before; InvalidateMeasure(); }
        e.Handled = true;
    }
}
