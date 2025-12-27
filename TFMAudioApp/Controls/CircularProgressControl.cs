using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace TFMAudioApp.Controls;

public class CircularProgressControl : ContentView
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(CircularProgressControl), 0.0,
            propertyChanged: OnProgressChanged);

    public static readonly BindableProperty ProgressColorProperty =
        BindableProperty.Create(nameof(ProgressColor), typeof(Color), typeof(CircularProgressControl), Colors.Blue,
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty BackgroundRingColorProperty =
        BindableProperty.Create(nameof(BackgroundRingColor), typeof(Color), typeof(CircularProgressControl), Colors.Gray,
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty RingThicknessProperty =
        BindableProperty.Create(nameof(RingThickness), typeof(double), typeof(CircularProgressControl), 8.0,
            propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(CircularProgressControl), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty SubTextProperty =
        BindableProperty.Create(nameof(SubText), typeof(string), typeof(CircularProgressControl), string.Empty,
            propertyChanged: OnTextChanged);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color ProgressColor
    {
        get => (Color)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    public Color BackgroundRingColor
    {
        get => (Color)GetValue(BackgroundRingColorProperty);
        set => SetValue(BackgroundRingColorProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    private readonly Grid _container;
    private readonly Ellipse _backgroundRing;
    private readonly Path _progressArc;
    private readonly Label _textLabel;
    private readonly Label _subTextLabel;

    public CircularProgressControl()
    {
        _container = new Grid();

        // Background ring
        _backgroundRing = new Ellipse
        {
            Stroke = new SolidColorBrush(BackgroundRingColor),
            StrokeThickness = RingThickness,
            Fill = Brush.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // Progress arc
        _progressArc = new Path
        {
            Stroke = new SolidColorBrush(ProgressColor),
            StrokeThickness = RingThickness,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brush.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // Center text
        _textLabel = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

        _subTextLabel = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            FontSize = 12,
            TextColor = Colors.Gray,
            Margin = new Thickness(0, 30, 0, 0)
        };

        _container.Children.Add(_backgroundRing);
        _container.Children.Add(_progressArc);
        _container.Children.Add(_textLabel);
        _container.Children.Add(_subTextLabel);

        Content = _container;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        UpdateProgressArc();
    }

    private static void OnProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CircularProgressControl control)
        {
            control.UpdateProgressArc();
        }
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CircularProgressControl control)
        {
            control.UpdateVisuals();
        }
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CircularProgressControl control)
        {
            control._textLabel.Text = control.Text;
            control._subTextLabel.Text = control.SubText;
        }
    }

    private void UpdateVisuals()
    {
        _backgroundRing.Stroke = new SolidColorBrush(BackgroundRingColor);
        _backgroundRing.StrokeThickness = RingThickness;
        _progressArc.Stroke = new SolidColorBrush(ProgressColor);
        _progressArc.StrokeThickness = RingThickness;
    }

    private void UpdateProgressArc()
    {
        var size = Math.Min(Width, Height);
        if (size <= 0) return;

        var centerX = size / 2;
        var centerY = size / 2;
        var radius = (size - RingThickness) / 2;

        var progress = Math.Clamp(Progress, 0, 1);
        var angle = progress * 360;

        if (angle <= 0)
        {
            _progressArc.Data = null;
            return;
        }

        if (angle >= 360)
        {
            angle = 359.99; // Prevent full circle issue
        }

        var startAngle = -90; // Start from top
        var endAngle = startAngle + angle;

        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        var startX = centerX + radius * Math.Cos(startRad);
        var startY = centerY + radius * Math.Sin(startRad);
        var endX = centerX + radius * Math.Cos(endRad);
        var endY = centerY + radius * Math.Sin(endRad);

        var largeArc = angle > 180;

        var pathData = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false
        };

        var arcSegment = new ArcSegment
        {
            Point = new Point(endX, endY),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = largeArc
        };

        figure.Segments.Add(arcSegment);
        pathData.Figures.Add(figure);

        _progressArc.Data = pathData;
    }
}
