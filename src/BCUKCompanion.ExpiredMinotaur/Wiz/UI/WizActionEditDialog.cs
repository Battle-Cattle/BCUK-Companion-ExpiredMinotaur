using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

public sealed class WizActionEditDialog : Window
{
    private sealed record WizActionKindOption(string Kind, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private static readonly WizActionKindOption TurnOnOption = new(WizTurnOnAction.ActionKind, "TurnOn");
    private static readonly WizActionKindOption TurnOffOption = new(WizTurnOffAction.ActionKind, "TurnOff");
    private static readonly WizActionKindOption ToggleOption = new(WizToggleAction.ActionKind, "Toggle");
    private static readonly WizActionKindOption SetBrightnessOption = new(WizSetBrightnessAction.ActionKind, "SetBrightness");
    private static readonly WizActionKindOption SetColorOption = new(WizSetColorAction.ActionKind, "SetColor");
    private static readonly WizActionKindOption SetColorTemperatureOption = new(WizSetColorTemperatureAction.ActionKind, "SetColorTemperature");
    private static readonly WizActionKindOption DelayOption = new(DelayAction.ActionKind, "Delay");

    private static readonly WizActionKindOption[] PlugKinds = [TurnOnOption, TurnOffOption, ToggleOption, DelayOption];
    private static readonly WizActionKindOption[] AllKinds =
        [TurnOnOption, TurnOffOption, ToggleOption, SetBrightnessOption, SetColorOption, SetColorTemperatureOption, DelayOption];

    private readonly ObservableCollection<WizDevice> devices;
    private readonly WizActionContext context;
    private readonly ComboBox deviceCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox actionKindCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock errorText = new() { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private readonly StackPanel brightnessPanel;
    private readonly Slider brightnessSlider = new() { Minimum = WizSetBrightnessAction.MinBrightness, Maximum = WizSetBrightnessAction.MaxBrightness, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock brightnessValueText = new();

    private readonly StackPanel colorPanel;
    private readonly Slider rSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider gSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider bSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Border colorSwatch = new() { Width = 32, Height = 32, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) };

    private readonly StackPanel colorTempPanel;
    private readonly Slider colorTempSlider = new() { Minimum = WizSetColorTemperatureAction.MinColorTemperatureKelvin, Maximum = WizSetColorTemperatureAction.MaxColorTemperatureKelvin, Value = 4000, TickFrequency = 100, IsSnapToTickEnabled = true };
    private readonly TextBlock colorTempValueText = new();

    private readonly StackPanel devicePanel;
    private readonly StackPanel delayPanel;
    private readonly TextBox delayBox = new() { Text = "5" };

    public IEventAction? Result { get; private set; }

    public WizActionEditDialog(IReadOnlyList<WizDevice> availableDevices, IEventAction? existing = null)
    {
        devices = new ObservableCollection<WizDevice>(availableDevices);
        // WizActionContext.Client is non-nullable, but this dialog only ever calls
        // IEventAction.Validate() (device-resolution/range checks) against this context,
        // never ExecuteAsync() — so this WizClient is never used to send anything. Its
        // constructor does no I/O of its own; it's a throwaway to satisfy the constructor.
        context = new WizActionContext(new WizClient(), devices);

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

        delayPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = $"Delay (seconds, {DelayAction.MinDelaySeconds}-{DelayAction.MaxDelaySeconds})" },
                delayBox,
            },
        };

        devicePanel = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Device" },
                deviceCombo,
            },
        };

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
                devicePanel,
                new TextBlock { Text = "Action" },
                actionKindCombo,
                brightnessPanel,
                colorPanel,
                colorTempPanel,
                delayPanel,
                errorText,
                buttonPanel,
            },
        };

        if (existing is not null)
        {
            var existingDeviceId = (existing as WizDeviceActionBase)?.DeviceId;
            deviceCombo.SelectedItem = devices.FirstOrDefault(d => d.Id == existingDeviceId);
            RefreshActionKinds();
            actionKindCombo.SelectedItem = (actionKindCombo.ItemsSource as IEnumerable<WizActionKindOption>)
                ?.FirstOrDefault(o => o.Kind == existing.Kind);

            switch (existing)
            {
                case WizSetBrightnessAction brightness:
                    brightnessSlider.Value = brightness.Brightness;
                    break;
                case WizSetColorAction color:
                    rSlider.Value = color.R;
                    gSlider.Value = color.G;
                    bSlider.Value = color.B;
                    break;
                case WizSetColorTemperatureAction colorTemp:
                    colorTempSlider.Value = colorTemp.ColorTemperatureKelvin;
                    break;
                case DelayAction delay:
                    delayBox.Text = delay.DelaySeconds.ToString();
                    break;
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
            ? new[] { DelayOption }
            : device.DeviceType == WizDeviceType.Plug
                ? PlugKinds
                : AllKinds.Where(kind => IsKindSupported(kind.Kind, device)).ToArray();

        var previouslySelected = actionKindCombo.SelectedItem as WizActionKindOption;
        actionKindCombo.ItemsSource = kinds;
        actionKindCombo.SelectedItem = previouslySelected is { } option && kinds.Contains(option)
            ? option
            : kinds.FirstOrDefault();
    }

    private static bool IsKindSupported(string kind, WizDevice device) => kind switch
    {
        WizSetBrightnessAction.ActionKind => device.SupportsDimming,
        WizSetColorAction.ActionKind => device.SupportsColor,
        WizSetColorTemperatureAction.ActionKind => device.SupportsColorTemperature,
        _ => true,
    };

    private void RefreshParameterPanelVisibility()
    {
        var kind = (actionKindCombo.SelectedItem as WizActionKindOption)?.Kind;
        brightnessPanel.Visibility = kind == SetBrightnessOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorPanel.Visibility = kind == SetColorOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorTempPanel.Visibility = kind == SetColorTemperatureOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        delayPanel.Visibility = kind == DelayOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        devicePanel.Visibility = kind == DelayOption.Kind ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateColorSwatch()
    {
        colorSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value));
    }

    private void OnOk()
    {
        var (action, error) = BuildAction();
        if (error is not null)
        {
            errorText.Text = error;
            return;
        }
        Result = action;
        DialogResult = true;
    }

    private (IEventAction? action, string? error) BuildAction()
    {
        if (actionKindCombo.SelectedItem is not WizActionKindOption option)
            return (null, "Select an action.");

        return option.Kind == DelayAction.ActionKind
            ? BuildDelayAction()
            : BuildDeviceAction(option);
    }

    private (IEventAction?, string?) BuildDelayAction()
    {
        if (!int.TryParse(delayBox.Text.Trim(), out var seconds))
            return (null, "Enter a whole number of seconds.");
        var action = new DelayAction { DelaySeconds = seconds };
        var errors = action.Validate(context);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }

    private (IEventAction?, string?) BuildDeviceAction(WizActionKindOption option)
    {
        if (SelectedDevice is not { } device)
            return (null, "Select a device and action.");
        IEventAction action = option.Kind switch
        {
            WizTurnOnAction.ActionKind => new WizTurnOnAction { DeviceId = device.Id },
            WizTurnOffAction.ActionKind => new WizTurnOffAction { DeviceId = device.Id },
            WizToggleAction.ActionKind => new WizToggleAction { DeviceId = device.Id },
            WizSetBrightnessAction.ActionKind => new WizSetBrightnessAction { DeviceId = device.Id, Brightness = (int)brightnessSlider.Value },
            WizSetColorAction.ActionKind => new WizSetColorAction { DeviceId = device.Id, R = (byte)rSlider.Value, G = (byte)gSlider.Value, B = (byte)bSlider.Value },
            WizSetColorTemperatureAction.ActionKind => new WizSetColorTemperatureAction { DeviceId = device.Id, ColorTemperatureKelvin = (int)colorTempSlider.Value },
            _ => throw new InvalidOperationException($"Unknown action kind \"{option.Kind}\"."),
        };
        var errors = action.Validate(context);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }
}
