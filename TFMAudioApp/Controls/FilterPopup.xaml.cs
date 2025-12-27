using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace TFMAudioApp.Controls;

public partial class FilterPopup : Popup
{
    private readonly List<string> _audioExtensions = new()
    {
        ".mp3", ".flac", ".ogg", ".opus", ".aac", ".wav", ".m4a", ".wma", ".ape"
    };

    private readonly List<string> _sortOptions = new() { "Name", "Date", "Size", "Type" };
    private readonly Dictionary<string, bool> _selectedExtensions = new();
    private readonly Dictionary<string, Border> _extensionBorders = new();
    private readonly Dictionary<string, Border> _sortBorders = new();
    private bool _sortDescending = true;
    private int _selectedSortIndex = 0;

    public new FilterResult? Result { get; private set; }

    public FilterPopup(FilterOptions? currentOptions = null)
    {
        InitializeComponent();
        InitializeExtensions(currentOptions?.SelectedExtensions);
        InitializeSortOptions(currentOptions?.SortIndex ?? 0);

        if (currentOptions != null)
        {
            ShowFoldersSwitch.IsToggled = currentOptions.ShowFolders;
            _sortDescending = currentOptions.SortDescending;
        }

        UpdateSortDirectionUI();
    }

    private void InitializeExtensions(List<string>? selectedExtensions)
    {
        ExtensionsContainer.Children.Clear();
        _extensionBorders.Clear();

        foreach (var ext in _audioExtensions)
        {
            var isSelected = selectedExtensions?.Contains(ext) ?? true;
            _selectedExtensions[ext] = isSelected;

            var chip = CreateExtensionChip(ext, isSelected);
            _extensionBorders[ext] = chip;
            ExtensionsContainer.Children.Add(chip);
        }
    }

    private void InitializeSortOptions(int selectedIndex)
    {
        SortOptionsContainer.Children.Clear();
        _sortBorders.Clear();
        _selectedSortIndex = selectedIndex;

        for (int i = 0; i < _sortOptions.Count; i++)
        {
            var option = _sortOptions[i];
            var isSelected = i == selectedIndex;
            var chip = CreateSortChip(option, i, isSelected);
            _sortBorders[option] = chip;
            SortOptionsContainer.Children.Add(chip);
        }
    }

    private Border CreateExtensionChip(string extension, bool isSelected)
    {
        var border = new Border
        {
            BackgroundColor = isSelected ? Color.FromArgb("#512BD4") : Color.FromArgb("#3D3D3D"),
            StrokeThickness = 0,
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 8, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 20 }
        };

        var label = new Label
        {
            Text = extension.ToUpper().TrimStart('.'),
            FontSize = 13,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };

        border.Content = label;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
            _selectedExtensions[extension] = !_selectedExtensions[extension];
            var selected = _selectedExtensions[extension];
            border.BackgroundColor = selected ? Color.FromArgb("#512BD4") : Color.FromArgb("#3D3D3D");
            ((Label)border.Content).FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        };
        border.GestureRecognizers.Add(tapGesture);

        return border;
    }

    private Border CreateSortChip(string option, int index, bool isSelected)
    {
        var border = new Border
        {
            BackgroundColor = isSelected ? Color.FromArgb("#512BD4") : Color.FromArgb("#3D3D3D"),
            StrokeThickness = 0,
            Padding = new Thickness(16, 10),
            Margin = new Thickness(0, 0, 8, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 20 }
        };

        var icon = option switch
        {
            "Name" => "Aa",
            "Date" => "📅",
            "Size" => "📊",
            "Type" => "📄",
            _ => ""
        };

        var stack = new HorizontalStackLayout { Spacing = 6 };
        stack.Children.Add(new Label
        {
            Text = icon,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center
        });
        stack.Children.Add(new Label
        {
            Text = option,
            FontSize = 13,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        });

        border.Content = stack;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
            // Deselect previous
            foreach (var kvp in _sortBorders)
            {
                kvp.Value.BackgroundColor = Color.FromArgb("#3D3D3D");
                if (kvp.Value.Content is HorizontalStackLayout sl && sl.Children.Count > 1)
                {
                    ((Label)sl.Children[1]).FontAttributes = FontAttributes.None;
                }
            }

            // Select current
            _selectedSortIndex = index;
            border.BackgroundColor = Color.FromArgb("#512BD4");
            if (border.Content is HorizontalStackLayout stack && stack.Children.Count > 1)
            {
                ((Label)stack.Children[1]).FontAttributes = FontAttributes.Bold;
            }
        };
        border.GestureRecognizers.Add(tapGesture);

        return border;
    }

    private void UpdateSortDirectionUI()
    {
        if (_sortDescending)
        {
            DescendingBorder.BackgroundColor = Color.FromArgb("#512BD4");
            AscendingBorder.BackgroundColor = Color.FromArgb("#3D3D3D");
        }
        else
        {
            AscendingBorder.BackgroundColor = Color.FromArgb("#512BD4");
            DescendingBorder.BackgroundColor = Color.FromArgb("#3D3D3D");
        }
    }

    private void OnAscendingTapped(object? sender, TappedEventArgs e)
    {
        _sortDescending = false;
        UpdateSortDirectionUI();
    }

    private void OnDescendingTapped(object? sender, TappedEventArgs e)
    {
        _sortDescending = true;
        UpdateSortDirectionUI();
    }

    private void OnClearAllFormatsClicked(object? sender, EventArgs e)
    {
        // Deselect all extensions
        foreach (var ext in _audioExtensions)
        {
            _selectedExtensions[ext] = false;
            if (_extensionBorders.TryGetValue(ext, out var border))
            {
                border.BackgroundColor = Color.FromArgb("#3D3D3D");
                ((Label)border.Content).FontAttributes = FontAttributes.None;
            }
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        Close(null);
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        // Reset to defaults
        ShowFoldersSwitch.IsToggled = true;
        _sortDescending = true;
        UpdateSortDirectionUI();

        // Select all extensions
        foreach (var ext in _audioExtensions)
        {
            _selectedExtensions[ext] = true;
            if (_extensionBorders.TryGetValue(ext, out var border))
            {
                border.BackgroundColor = Color.FromArgb("#512BD4");
                ((Label)border.Content).FontAttributes = FontAttributes.Bold;
            }
        }

        // Reset sort to Name
        _selectedSortIndex = 0;
        InitializeSortOptions(0);
    }

    private void OnApplyClicked(object? sender, EventArgs e)
    {
        Result = new FilterResult
        {
            ShowFolders = ShowFoldersSwitch.IsToggled,
            SelectedExtensions = _selectedExtensions.Where(x => x.Value).Select(x => x.Key).ToList(),
            SortBy = _selectedSortIndex switch
            {
                0 => "name",
                1 => "date",
                2 => "size",
                3 => "type",
                _ => "name"
            },
            SortDescending = _sortDescending
        };

        Close(Result);
    }
}

public class FilterOptions
{
    public bool ShowFolders { get; set; } = true;
    public List<string>? SelectedExtensions { get; set; }
    public int SortIndex { get; set; }
    public bool SortDescending { get; set; } = true;
}

public class FilterResult
{
    public bool ShowFolders { get; set; }
    public List<string> SelectedExtensions { get; set; } = new();
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
}
