using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

public sealed class WizActionEditDialog : Window
{
    private readonly ObservableCollection<WizDevice> devices;
    private readonly ComboBox deviceCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox actionKindCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock errorText = new() { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private readonly StackPanel brightnessPanel;
    private readonly Slider brightnessSlider = new() { Minimum = WizAction.MinBrightness, Maximum = WizAction.MaxBrightness, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock brightnessValueText = new();

    private readonly StackPanel colorPanel;
    private readonly Slider rSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider gSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider bSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Border colorSwatch = new() { Width = 32, Height = 32, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) };

    private readonly StackPanel colorTempPanel;
    private readonly Slider colorTempSlider = new() { Minimum = WizAction.MinColorTemperatureKelvin, Maximum = WizAction.MaxColorTemperatureKelvin, Value = 4000, TickFrequency = 100, IsSnapToTickEnabled = true };
    private readonly TextBlock colorTempValueText = new();

    public WizAction? Result { get; private set; }

    public WizActionEditDialog(IReadOnlyList<WizDevice> availableDevices, WizAction? existing = null)
    {
        devices = new ObservableCollection<WizDevice>(availableDevices);

        Title = existing is null ? "Add Action" : "Edit Action";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        deviceCombo.ItemsSource = devices;
        deviceCombo.DisplayMemberPath = nameof(WizDevice.Name);
        deviceCombo.SelectionChanged += (_, _) => RefreshActionKinds();

        actionKindCombo.SelectionChanged += (_, _) => RefreshParameterPanelVisibility();

        brightnessSlider.ValueChanged += (_, _) => brightnessValueText.Text = $"{(int)brightnessSlider.Value}%";
        brightnessValueText.Text = $"{(int)brightnessSlider.Value}%";
        brightnessPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Brightness" },
                new DockPanel
                {
                    Children = { brightnessSlider, brightnessValueText },
                },
            },
        };
        DockPanel.SetDock(brightnessValueText, Dock.Right);

        rSlider.ValueChanged += (_, _) => UpdateColorSwatch();
        gSlider.ValueChanged += (_, _) => UpdateColorSwatch();
        bSlider.ValueChanged += (_, _) => UpdateColorSwatch();
        UpdateColorSwatch();
        colorPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Color" },
                new DockPanel { Children = { new TextBlock { Text = "R", Width = 16 }, rSlider } },
                new DockPanel { Children = { new TextBlock { Text = "G", Width = 16 }, gSlider } },
                new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "B", Width = 16 },
                        bSlider,
                        colorSwatch,
                    },
                },
            },
        };
        foreach (var dock in colorPanel.Children.OfType<DockPanel>())
        {
            DockPanel.SetDock(dock.Children[0], Dock.Left);
        }
        DockPanel.SetDock(colorSwatch, Dock.Right);

        colorTempSlider.ValueChanged += (_, _) => colorTempValueText.Text = $"{(int)colorTempSlider.Value}K";
        colorTempValueText.Text = $"{(int)colorTempSlider.Value}K";
        colorTempPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Color Temperature" },
                new DockPanel { Children = { colorTempSlider, colorTempValueText } },
            },
        };
        DockPanel.SetDock(colorTempValueText, Dock.Right);

        var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => OnOk();

        var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        cancelButton.Click += (_, _) => { DialogResult = false; };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { okButton, cancelButton },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = "Device" },
                deviceCombo,
                new TextBlock { Text = "Action" },
                actionKindCombo,
                brightnessPanel,
                colorPanel,
                colorTempPanel,
                errorText,
                buttonPanel,
            },
        };

        if (existing is not null)
        {
            deviceCombo.SelectedItem = devices.FirstOrDefault(d => d.Id == existing.DeviceId);
            RefreshActionKinds();
            actionKindCombo.SelectedItem = existing.ActionKind;
            if (existing.Brightness is { } brightness)
            {
                brightnessSlider.Value = brightness;
            }
            if (existing.R is { } r && existing.G is { } g && existing.B is { } b)
            {
                rSlider.Value = r;
                gSlider.Value = g;
                bSlider.Value = b;
            }
            if (existing.ColorTemperatureKelvin is { } temp)
            {
                colorTempSlider.Value = temp;
            }
        }
        else if (devices.Count > 0)
        {
            deviceCombo.SelectedIndex = 0;
        }

        RefreshParameterPanelVisibility();
    }

    private WizDevice? SelectedDevice => deviceCombo.SelectedItem as WizDevice;

    private void RefreshActionKinds()
    {
        var device = SelectedDevice;
        var kinds = device is null
            ? Array.Empty<WizActionKind>()
            : device.DeviceType == WizDeviceType.Plug
                ? new[] { WizActionKind.TurnOn, WizActionKind.TurnOff, WizActionKind.Toggle }
                : Enum.GetValues<WizActionKind>()
                    .Where(kind => IsKindSupported(kind, device))
                    .ToArray();

        var previouslySelected = actionKindCombo.SelectedItem as WizActionKind?;
        actionKindCombo.ItemsSource = kinds;
        actionKindCombo.SelectedItem = previouslySelected is { } kind && kinds.Contains(kind)
            ? kind
            : kinds.FirstOrDefault();
    }

    private static bool IsKindSupported(WizActionKind kind, WizDevice device)
    {
        if (kind is WizActionKind.TurnOn or WizActionKind.TurnOff or WizActionKind.Toggle)
        {
            return true;
        }

        return kind switch
        {
            WizActionKind.SetBrightness => device.SupportsDimming,
            WizActionKind.SetColor => device.SupportsColor,
            WizActionKind.SetColorTemperature => device.SupportsColorTemperature,
            _ => false,
        };
    }

    private void RefreshParameterPanelVisibility()
    {
        var kind = actionKindCombo.SelectedItem as WizActionKind?;
        brightnessPanel.Visibility = kind == WizActionKind.SetBrightness ? Visibility.Visible : Visibility.Collapsed;
        colorPanel.Visibility = kind == WizActionKind.SetColor ? Visibility.Visible : Visibility.Collapsed;
        colorTempPanel.Visibility = kind == WizActionKind.SetColorTemperature ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateColorSwatch()
    {
        colorSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value));
    }

    private void OnOk()
    {
        var device = SelectedDevice;
        if (device is null || actionKindCombo.SelectedItem is not WizActionKind kind)
        {
            errorText.Text = "Select a device and action.";
            return;
        }

        var action = new WizAction(
            device.Id,
            kind,
            Brightness: kind == WizActionKind.SetBrightness ? (int)brightnessSlider.Value : null,
            R: kind == WizActionKind.SetColor ? (byte)rSlider.Value : null,
            G: kind == WizActionKind.SetColor ? (byte)gSlider.Value : null,
            B: kind == WizActionKind.SetColor ? (byte)bSlider.Value : null,
            ColorTemperatureKelvin: kind == WizActionKind.SetColorTemperature ? (int)colorTempSlider.Value : null);

        var errors = WizAction.Validate(action, device);
        if (errors.Count > 0)
        {
            errorText.Text = string.Join("\n", errors);
            return;
        }

        Result = action;
        DialogResult = true;
    }
}
