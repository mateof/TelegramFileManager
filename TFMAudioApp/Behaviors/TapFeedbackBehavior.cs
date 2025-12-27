namespace TFMAudioApp.Behaviors;

/// <summary>
/// A behavior that provides visual feedback (opacity animation) when an element is tapped.
/// This works with TapGestureRecognizer without requiring RelativeSource bindings.
/// </summary>
public class TapFeedbackBehavior : Behavior<View>
{
    private View? _associatedView;
    private TapGestureRecognizer? _tapGesture;

    public static readonly BindableProperty PressedOpacityProperty =
        BindableProperty.Create(nameof(PressedOpacity), typeof(double), typeof(TapFeedbackBehavior), 0.6);

    public static readonly BindableProperty AnimationDurationProperty =
        BindableProperty.Create(nameof(AnimationDuration), typeof(uint), typeof(TapFeedbackBehavior), (uint)100);

    public double PressedOpacity
    {
        get => (double)GetValue(PressedOpacityProperty);
        set => SetValue(PressedOpacityProperty, value);
    }

    public uint AnimationDuration
    {
        get => (uint)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedView = bindable;

        // Find existing TapGestureRecognizer or add press handlers
        foreach (var gesture in bindable.GestureRecognizers)
        {
            if (gesture is TapGestureRecognizer tap)
            {
                _tapGesture = tap;
                break;
            }
        }

        // Use PointerGestureRecognizer for better press detection
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        pointerGesture.PointerExited += OnPointerExited;
        bindable.GestureRecognizers.Add(pointerGesture);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);

        // Remove pointer gesture
        var pointerGestures = bindable.GestureRecognizers.OfType<PointerGestureRecognizer>().ToList();
        foreach (var pg in pointerGestures)
        {
            pg.PointerPressed -= OnPointerPressed;
            pg.PointerReleased -= OnPointerReleased;
            pg.PointerExited -= OnPointerExited;
            bindable.GestureRecognizers.Remove(pg);
        }

        _associatedView = null;
        _tapGesture = null;
    }

    private async void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        await _associatedView.FadeTo(PressedOpacity, AnimationDuration / 2, Easing.CubicOut);
    }

    private async void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        await _associatedView.FadeTo(1.0, AnimationDuration, Easing.CubicIn);
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        await _associatedView.FadeTo(1.0, AnimationDuration, Easing.CubicIn);
    }
}
