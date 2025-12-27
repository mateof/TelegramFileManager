namespace TFMAudioApp.Controls;

/// <summary>
/// A skeleton loading placeholder with shimmer animation
/// </summary>
public class SkeletonView : ContentView
{
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(SkeletonView), 4.0,
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(SkeletonView), true,
            propertyChanged: OnIsActiveChanged);

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private readonly BoxView _background;
    private readonly BoxView _shimmer;
    private CancellationTokenSource? _animationCts;
    private bool _isAnimating;

    public SkeletonView()
    {
        var grid = new Grid();

        _background = new BoxView
        {
            Color = Color.FromArgb("#2A2A2A"),
            CornerRadius = new CornerRadius(CornerRadius),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _shimmer = new BoxView
        {
            Color = Color.FromArgb("#3A3A3A"),
            CornerRadius = new CornerRadius(CornerRadius),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Opacity = 0
        };

        grid.Children.Add(_background);
        grid.Children.Add(_shimmer);

        Content = grid;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent != null && IsActive)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SkeletonView control)
        {
            control._background.CornerRadius = new CornerRadius(control.CornerRadius);
            control._shimmer.CornerRadius = new CornerRadius(control.CornerRadius);
        }
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SkeletonView control)
        {
            if ((bool)newValue)
            {
                control.StartAnimation();
            }
            else
            {
                control.StopAnimation();
            }
        }
    }

    private void StartAnimation()
    {
        if (_isAnimating) return;
        _isAnimating = true;

        _animationCts?.Cancel();
        _animationCts = new CancellationTokenSource();
        var token = _animationCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (token.IsCancellationRequested) return;
                        await _shimmer.FadeTo(1, 600, Easing.SinInOut);
                    });

                    if (token.IsCancellationRequested) break;

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (token.IsCancellationRequested) return;
                        await _shimmer.FadeTo(0, 600, Easing.SinInOut);
                    });
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopAnimation()
    {
        _animationCts?.Cancel();
        _isAnimating = false;
        _shimmer.Opacity = 0;
    }
}

/// <summary>
/// A skeleton item that mimics a list item with icon, title and subtitle
/// </summary>
public class SkeletonListItem : ContentView
{
    public static readonly BindableProperty ShowIconProperty =
        BindableProperty.Create(nameof(ShowIcon), typeof(bool), typeof(SkeletonListItem), true);

    public static readonly BindableProperty ShowSubtitleProperty =
        BindableProperty.Create(nameof(ShowSubtitle), typeof(bool), typeof(SkeletonListItem), true);

    public static readonly BindableProperty ShowTrailingProperty =
        BindableProperty.Create(nameof(ShowTrailing), typeof(bool), typeof(SkeletonListItem), false);

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public bool ShowSubtitle
    {
        get => (bool)GetValue(ShowSubtitleProperty);
        set => SetValue(ShowSubtitleProperty, value);
    }

    public bool ShowTrailing
    {
        get => (bool)GetValue(ShowTrailingProperty);
        set => SetValue(ShowTrailingProperty, value);
    }

    public SkeletonListItem()
    {
        BuildContent();
    }

    private void BuildContent()
    {
        var frame = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E1E1E"),
            BorderColor = Colors.Transparent,
            CornerRadius = 8,
            Padding = new Thickness(15),
            Margin = new Thickness(10, 5)
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(48) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 15
        };

        // Icon skeleton
        var iconSkeleton = new SkeletonView
        {
            WidthRequest = 48,
            HeightRequest = 48,
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        iconSkeleton.SetBinding(IsVisibleProperty, new Binding(nameof(ShowIcon), source: this));
        Grid.SetColumn(iconSkeleton, 0);

        // Text content
        var textStack = new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        var titleSkeleton = new SkeletonView
        {
            HeightRequest = 16,
            WidthRequest = 180,
            CornerRadius = 4,
            HorizontalOptions = LayoutOptions.Start
        };

        var subtitleSkeleton = new SkeletonView
        {
            HeightRequest = 12,
            WidthRequest = 100,
            CornerRadius = 4,
            HorizontalOptions = LayoutOptions.Start
        };
        subtitleSkeleton.SetBinding(IsVisibleProperty, new Binding(nameof(ShowSubtitle), source: this));

        textStack.Children.Add(titleSkeleton);
        textStack.Children.Add(subtitleSkeleton);
        Grid.SetColumn(textStack, 1);

        // Trailing skeleton
        var trailingSkeleton = new SkeletonView
        {
            WidthRequest = 40,
            HeightRequest = 16,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };
        trailingSkeleton.SetBinding(IsVisibleProperty, new Binding(nameof(ShowTrailing), source: this));
        Grid.SetColumn(trailingSkeleton, 2);

        grid.Children.Add(iconSkeleton);
        grid.Children.Add(textStack);
        grid.Children.Add(trailingSkeleton);

        frame.Content = grid;
        Content = frame;
    }
}

/// <summary>
/// A container that shows skeleton items while loading
/// </summary>
public class SkeletonListContainer : ContentView
{
    public static readonly BindableProperty ItemCountProperty =
        BindableProperty.Create(nameof(ItemCount), typeof(int), typeof(SkeletonListContainer), 5,
            propertyChanged: OnItemCountChanged);

    public static readonly BindableProperty ShowIconProperty =
        BindableProperty.Create(nameof(ShowIcon), typeof(bool), typeof(SkeletonListContainer), true);

    public static readonly BindableProperty ShowSubtitleProperty =
        BindableProperty.Create(nameof(ShowSubtitle), typeof(bool), typeof(SkeletonListContainer), true);

    public static readonly BindableProperty ShowTrailingProperty =
        BindableProperty.Create(nameof(ShowTrailing), typeof(bool), typeof(SkeletonListContainer), false);

    public int ItemCount
    {
        get => (int)GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    public bool ShowIcon
    {
        get => (bool)GetValue(ShowIconProperty);
        set => SetValue(ShowIconProperty, value);
    }

    public bool ShowSubtitle
    {
        get => (bool)GetValue(ShowSubtitleProperty);
        set => SetValue(ShowSubtitleProperty, value);
    }

    public bool ShowTrailing
    {
        get => (bool)GetValue(ShowTrailingProperty);
        set => SetValue(ShowTrailingProperty, value);
    }

    private readonly VerticalStackLayout _container;

    public SkeletonListContainer()
    {
        _container = new VerticalStackLayout
        {
            Spacing = 0
        };
        Content = _container;
        BuildItems();
    }

    private static void OnItemCountChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SkeletonListContainer control)
        {
            control.BuildItems();
        }
    }

    private void BuildItems()
    {
        _container.Children.Clear();

        for (int i = 0; i < ItemCount; i++)
        {
            var item = new SkeletonListItem();
            item.SetBinding(SkeletonListItem.ShowIconProperty, new Binding(nameof(ShowIcon), source: this));
            item.SetBinding(SkeletonListItem.ShowSubtitleProperty, new Binding(nameof(ShowSubtitle), source: this));
            item.SetBinding(SkeletonListItem.ShowTrailingProperty, new Binding(nameof(ShowTrailing), source: this));
            _container.Children.Add(item);
        }
    }
}
