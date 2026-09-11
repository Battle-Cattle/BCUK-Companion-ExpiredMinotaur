using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BCUKCompanion.ExpiredMinotaur.UI;

/// <summary>
/// Single Add/Edit Action dialog covering every registered action kind (Wiz device actions,
/// the Treadmill's NudgeSpeed, and Delay), so one <see cref="EventActionMapping"/> can mix
/// action kinds from different integrations instead of each integration needing its own
/// dialog. A top-level "Category" choice picks which integration's parameters to show; the
/// Wiz category then reuses the original device-first flow (kind choices depend on the
/// selected device's type/capabilities).
/// </summary>
public sealed class ActionEditDialog : Window
{
    private sealed record CategoryOption(string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record WizActionKindOption(string Kind, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private static readonly CategoryOption WizCategory = new("Wiz Device");
    private static readonly CategoryOption TreadmillCategory = new("Treadmill");
    private static readonly CategoryOption DelayCategory = new("Delay");
    private static readonly CategoryOption[] AllCategories = [WizCategory, TreadmillCategory, DelayCategory];

    private static readonly WizActionKindOption TurnOnOption = new(WizTurnOnAction.ActionKind, "TurnOn");
    private static readonly WizActionKindOption TurnOffOption = new(WizTurnOffAction.ActionKind, "TurnOff");
    private static readonly WizActionKindOption ToggleOption = new(WizToggleAction.ActionKind, "Toggle");
    private static readonly WizActionKindOption SetBrightnessOption = new(WizSetBrightnessAction.ActionKind, "SetBrightness");
    private static readonly WizActionKindOption SetColorOption = new(WizSetColorAction.ActionKind, "SetColor");
    private static readonly WizActionKindOption SetColorTemperatureOption = new(WizSetColorTemperatureAction.ActionKind, "SetColorTemperature");

    private static readonly WizActionKindOption[] PlugKinds = [TurnOnOption, TurnOffOption, ToggleOption];
    private static readonly WizActionKindOption[] AllWizKinds =
        [TurnOnOption, TurnOffOption, ToggleOption, SetBrightnessOption, SetColorOption, SetColorTemperatureOption];

    private readonly ObservableCollection<WizDevice> devices;
    private readonly WizActionContext wizContext;
    private readonly TreadmillActionContext treadmillContext;
    private readonly IEventActionContext validationContext;

    private readonly ComboBox categoryCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox deviceCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox wizKindCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock errorText = new() { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private readonly StackPanel devicePanel;
    private readonly StackPanel wizKindPanel;

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

    private readonly StackPanel deltaPanel;
    private readonly TextBox deltaBox = new() { Text = "1.0" };

    private readonly StackPanel delayPanel;
    private readonly TextBox delayBox = new() { Text = "5" };

    public IEventAction? Result { get; private set; }

    public ActionEditDialog(IReadOnlyList<WizDevice> availableDevices, TreadmillClient treadmillClient, IEventAction? existing = null)
    {
        devices = new ObservableCollection<WizDevice>(availableDevices);
        // These are only used for IEventAction.Validate() below, never ExecuteAsync(), so a
        // throwaway WizClient (no I/O in its constructor) is safe; the real TreadmillClient is
        // passed in so Validate() sees accurate state if that ever matters.
        wizContext = new WizActionContext(new WizClient(), devices);
        treadmillContext = new TreadmillActionContext(treadmillClient);
        validationContext = new ActionsContext(wizContext, treadmillContext);

        Title = existing is null ? "Add Action" : "Edit Action";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        categoryCombo.ItemsSource = AllCategories;
        categoryCombo.SelectionChanged += (_, _) => RefreshCategoryVisibility();

        deviceCombo.ItemsSource = devices;
        deviceCombo.DisplayMemberPath = nameof(WizDevice.Name);
        deviceCombo.SelectionChanged += (_, _) => RefreshWizKinds();

        wizKindCombo.SelectionChanged += (_, _) => RefreshWizParameterPanelVisibility();

        devicePanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "Device" }, deviceCombo },
        };

        wizKindPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "Wiz action" }, wizKindCombo },
        };

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

        deltaPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Speed delta (km/h, negative to slow down)" },
                deltaBox,
            },
        };

        delayPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = $"Delay (seconds, {DelayAction.MinDelaySeconds}-{DelayAction.MaxDelaySeconds})" },
                delayBox,
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
                new TextBlock { Text = "Category" },
                categoryCombo,
                devicePanel,
                wizKindPanel,
                brightnessPanel,
                colorPanel,
                colorTempPanel,
                deltaPanel,
                delayPanel,
                errorText,
                buttonPanel,
            },
        };

        InitializeFromExisting(existing);
        RefreshCategoryVisibility();
    }

    private WizDevice? SelectedDevice => deviceCombo.SelectedItem as WizDevice;

    private void InitializeFromExisting(IEventAction? existing)
    {
        switch (existing)
        {
            case WizDeviceActionBase wiz:
                categoryCombo.SelectedItem = WizCategory;
                deviceCombo.SelectedItem = devices.FirstOrDefault(d => d.Id == wiz.DeviceId);
                RefreshWizKinds();
                wizKindCombo.SelectedItem = (wizKindCombo.ItemsSource as IEnumerable<WizActionKindOption>)
                    ?.FirstOrDefault(o => o.Kind == wiz.Kind);

                switch (wiz)
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
                }
                break;

            case TreadmillNudgeSpeedAction nudge:
                categoryCombo.SelectedItem = TreadmillCategory;
                deltaBox.Text = nudge.DeltaKmh.ToString("R", CultureInfo.InvariantCulture);
                break;

            case DelayAction delay:
                categoryCombo.SelectedItem = DelayCategory;
                delayBox.Text = delay.DelaySeconds.ToString();
                break;

            default:
                categoryCombo.SelectedItem = devices.Count > 0 ? WizCategory : TreadmillCategory;
                if (devices.Count > 0)
                {
                    deviceCombo.SelectedIndex = 0;
                }
                RefreshWizKinds();
                break;
        }
    }

    private void RefreshWizKinds()
    {
        var device = SelectedDevice;
        var kinds = device is null
            ? Array.Empty<WizActionKindOption>()
            : device.DeviceType == WizDeviceType.Plug
                ? PlugKinds
                : AllWizKinds.Where(kind => IsKindSupported(kind.Kind, device)).ToArray();

        var previouslySelected = wizKindCombo.SelectedItem as WizActionKindOption;
        wizKindCombo.ItemsSource = kinds;
        wizKindCombo.SelectedItem = previouslySelected is { } option && kinds.Contains(option)
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

    private void RefreshCategoryVisibility()
    {
        var category = categoryCombo.SelectedItem as CategoryOption;
        devicePanel.Visibility = category == WizCategory ? Visibility.Visible : Visibility.Collapsed;
        wizKindPanel.Visibility = category == WizCategory ? Visibility.Visible : Visibility.Collapsed;
        deltaPanel.Visibility = category == TreadmillCategory ? Visibility.Visible : Visibility.Collapsed;
        delayPanel.Visibility = category == DelayCategory ? Visibility.Visible : Visibility.Collapsed;
        RefreshWizParameterPanelVisibility();
    }

    private void RefreshWizParameterPanelVisibility()
    {
        var category = categoryCombo.SelectedItem as CategoryOption;
        var kind = category == WizCategory ? (wizKindCombo.SelectedItem as WizActionKindOption)?.Kind : null;
        brightnessPanel.Visibility = kind == SetBrightnessOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorPanel.Visibility = kind == SetColorOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorTempPanel.Visibility = kind == SetColorTemperatureOption.Kind ? Visibility.Visible : Visibility.Collapsed;
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
        if (categoryCombo.SelectedItem is not CategoryOption category)
            return (null, "Select a category.");

        return category switch
        {
            _ when category == WizCategory => BuildWizAction(),
            _ when category == TreadmillCategory => BuildNudgeSpeedAction(),
            _ when category == DelayCategory => BuildDelayAction(),
            _ => (null, "Select a category."),
        };
    }

    private (IEventAction?, string?) BuildWizAction()
    {
        if (SelectedDevice is not { } device)
            return (null, "Select a device.");
        if (wizKindCombo.SelectedItem is not WizActionKindOption option)
            return (null, "Select an action.");

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
        var errors = action.Validate(validationContext);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }

    private (IEventAction?, string?) BuildNudgeSpeedAction()
    {
        if (!double.TryParse(deltaBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var delta)
            || !double.IsFinite(delta))
            return (null, "Enter a numeric speed delta.");
        var action = new TreadmillNudgeSpeedAction { DeltaKmh = delta };
        var errors = action.Validate(validationContext);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }

    private (IEventAction?, string?) BuildDelayAction()
    {
        if (!int.TryParse(delayBox.Text.Trim(), out var seconds))
            return (null, "Enter a whole number of seconds.");
        var action = new DelayAction { DelaySeconds = seconds };
        var errors = action.Validate(validationContext);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }
}
