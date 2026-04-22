using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ApocalypseLauncher.Core.Services;

namespace ApocalypseLauncher.Controls;

public class SkinPreview3DControl : Control
{
    public static readonly StyledProperty<string?> SkinPathProperty =
        AvaloniaProperty.Register<SkinPreview3DControl, string?>(nameof(SkinPath));

    public static readonly StyledProperty<bool> IsSlimModelProperty =
        AvaloniaProperty.Register<SkinPreview3DControl, bool>(nameof(IsSlimModel));

    private Bitmap? _renderedBitmap;
    private bool _isDragging;
    private Point _lastPointerPosition;
    private bool _needsRender = true;
    private float _yaw = -20f;
    private float _pitch = 12f;

    public string? SkinPath
    {
        get => GetValue(SkinPathProperty);
        set => SetValue(SkinPathProperty, value);
    }

    public bool IsSlimModel
    {
        get => GetValue(IsSlimModelProperty);
        set => SetValue(IsSlimModelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SkinPathProperty ||
            change.Property == IsSlimModelProperty ||
            change.Property == BoundsProperty)
        {
            _needsRender = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        var delta = currentPosition - _lastPointerPosition;
        _lastPointerPosition = currentPosition;

        _yaw += (float)(delta.X * 0.8);
        _pitch = Math.Clamp(_pitch - (float)(delta.Y * 0.35), -35f, 35f);

        _needsRender = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleasePointer(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void ReleasePointer(PointerEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        EnsureRenderedBitmap();

        if (_renderedBitmap == null)
        {
            return;
        }

        var sourceRect = new Rect(0, 0, _renderedBitmap.PixelSize.Width, _renderedBitmap.PixelSize.Height);
        context.DrawImage(_renderedBitmap, sourceRect, Bounds);
    }

    private void EnsureRenderedBitmap()
    {
        if (!_needsRender)
        {
            return;
        }

        _needsRender = false;
        ReplaceBitmap(null);

        if (string.IsNullOrWhiteSpace(SkinPath) || !File.Exists(SkinPath))
        {
            return;
        }

        var width = Math.Max(96, (int)Math.Ceiling(Bounds.Width));
        var height = Math.Max(160, (int)Math.Ceiling(Bounds.Height));

        try
        {
            var renderedBytes = SkinRenderer.RenderInteractiveSkin3D(SkinPath, IsSlimModel, _yaw, _pitch, width, height);
            using var stream = new MemoryStream(renderedBytes);
            ReplaceBitmap(new Bitmap(stream));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkinPreview3DControl] Ошибка рендера: {ex.Message}");
        }
    }

    private void ReplaceBitmap(Bitmap? bitmap)
    {
        _renderedBitmap?.Dispose();
        _renderedBitmap = bitmap;
    }
}
