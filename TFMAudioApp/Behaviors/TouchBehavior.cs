using Microsoft.Maui.Controls;

namespace TFMAudioApp.Behaviors;

/// <summary>
/// Behavior that adds touch feedback (press effect) to any View.
/// Apply this to Border, Frame, Grid, or any container to get visual feedback on touch.
/// </summary>
public class TouchBehavior : Behavior<View>
{
    private View? _element;
    private double _originalOpacity;
    private double _originalScale;

    /// <summary>
    /// Scale factor when pressed (default 0.97 = slight shrink)
    /// </summary>
    public double PressedScale { get; set; } = 0.97;

    /// <summary>
    /// Opacity when pressed (default 0.8)
    /// </summary>
    public double PressedOpacity { get; set; } = 0.8;

    /// <summary>
    /// Duration of the press animation in milliseconds (default 80ms)
    /// </summary>
    public uint AnimationDuration { get; set; } = 80;

    /// <summary>
    /// Whether to use native ripple effect on Android (requires specific setup)
    /// </summary>
    public bool UseNativeEffect { get; set; } = false;

    protected override void OnAttachedTo(View element)
    {
        base.OnAttachedTo(element);
        _element = element;

        // Store original values
        _originalOpacity = element.Opacity;
        _originalScale = element.Scale;

        // Add pointer/touch handlers
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        pointerGesture.PointerExited += OnPointerExited;
        element.GestureRecognizers.Add(pointerGesture);
    }

    protected override void OnDetachingFrom(View element)
    {
        base.OnDetachingFrom(element);

        // Remove gesture recognizers
        var toRemove = element.GestureRecognizers
            .OfType<PointerGestureRecognizer>()
            .ToList();

        foreach (var gesture in toRemove)
        {
            gesture.PointerPressed -= OnPointerPressed;
            gesture.PointerReleased -= OnPointerReleased;
            gesture.PointerExited -= OnPointerExited;
            element.GestureRecognizers.Remove(gesture);
        }

        _element = null;
    }

    private async void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_element == null) return;

        try
        {
            // Animate to pressed state
            await Task.WhenAll(
                _element.ScaleTo(PressedScale, AnimationDuration, Easing.CubicOut),
                _element.FadeTo(PressedOpacity, AnimationDuration, Easing.CubicOut)
            );
        }
        catch
        {
            // Animation cancelled, ignore
        }
    }

    private async void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        await AnimateToNormal();
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        await AnimateToNormal();
    }

    private async Task AnimateToNormal()
    {
        if (_element == null) return;

        try
        {
            // Animate back to normal state
            await Task.WhenAll(
                _element.ScaleTo(_originalScale, AnimationDuration, Easing.CubicOut),
                _element.FadeTo(_originalOpacity, AnimationDuration, Easing.CubicOut)
            );
        }
        catch
        {
            // Animation cancelled, reset immediately
            _element.Scale = _originalScale;
            _element.Opacity = _originalOpacity;
        }
    }
}
