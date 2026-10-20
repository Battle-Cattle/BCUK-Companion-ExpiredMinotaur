using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp.Actions;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

/// <summary>
/// The "Wiz Device" category of the Add/Edit Action dialog: device first, then the action kinds
/// that device's type/capabilities support, then that kind's parameters.
/// </summary>
public sealed class WizActionEditor : EventActionEditor
{
    /// <summary>One entry in the action-kind picker.</summary>
    /// <param name="Kind">The action kind it creates.</param>
    /// <param name="DisplayName">The name shown in the picker.</param>
    private sealed record WizActionKindOption(string Kind, string DisplayName)
    {
        /// <summary>The name shown in the picker.</summary>
        public override string ToString() => DisplayName;
    }

    private static readonly WizActionKindOption TurnOnOption = new(WizTurnOnAction.ActionKind, "TurnOn");
    private static readonly WizActionKindOption TurnOffOption = new(WizTurnOffAction.ActionKind, "TurnOff");
    private static readonly WizActionKindOption ToggleOption = new(WizToggleAction.ActionKind, "Toggle");
    private static readonly WizActionKindOption SetBrightnessOption = new(WizSetBrightnessAction.ActionKind, "SetBrightness");
    private static readonly WizActionKindOption SetColorOption = new(WizSetColorAction.ActionKind, "SetColor");
    private static readonly WizActionKindOption SetColorTemperatureOption = new(WizSetColorTemperatureAction.ActionKind, "SetColorTemperature");

    private static readonly WizActionKindOption[] AllWizKinds =
        [TurnOnOption, TurnOffOption, ToggleOption, SetBrightnessOption, SetColorOption, SetColorTemperatureOption];

    private readonly IReadOnlyList<WizDevice> devices;

    private readonly ComboBox deviceCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox wizKindCombo = new() { Margin = new Thickness(0, 0, 0, 8) };

    private readonly StackPanel brightnessPanel;
    private readonly Slider brightnessSlider = new() { Minimum = WizLightSettingAction.BrightnessSetting.Channels[0].Min, Maximum = WizLightSettingAction.BrightnessSetting.Channels[0].Max, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock brightnessValueText = new();

    private readonly StackPanel colorPanel;
    private readonly Slider rSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider gSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Slider bSlider = new() { Minimum = 0, Maximum = 255, Value = 255 };
    private readonly Border colorSwatch = new() { Width = 32, Height = 32, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) };

    private readonly StackPanel colorTempPanel;
    private readonly Slider colorTempSlider = new() { Minimum = WizLightSettingAction.ColorTemperatureSetting.Channels[0].Min, Maximum = WizLightSettingAction.ColorTemperatureSetting.Channels[0].Max, Value = 4000, TickFrequency = 100, IsSnapToTickEnabled = true };
    private readonly TextBlock colorTempValueText = new();

    /// <summary>Creates the editor, preselecting the first device if there is one.</summary>
    /// <param name="devices">The configured Wiz devices an action can target.</param>
    public WizActionEditor(IReadOnlyList<WizDevice> devices)
    {
        this.devices = devices;

        deviceCombo.ItemsSource = devices;
        deviceCombo.DisplayMemberPath = nameof(WizDevice.Name);
        deviceCombo.SelectionChanged += (_, _) => RefreshWizKinds();

        wizKindCombo.SelectionChanged += (_, _) => RefreshParameterPanelVisibility();

        brightnessSlider.ValueChanged += (_, _) => brightnessValueText.Text = $"{(int)brightnessSlider.Value}%";
        brightnessValueText.Text = $"{(int)brightnessSlider.Value}%";
        brightnessPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Brightness" },
                new DockPanel { Children = { brightnessSlider, brightnessValueText } },
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
                new DockPanel { Children = { new TextBlock { Text = "B", Width = 16 }, bSlider, colorSwatch } },
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

        View = new StackPanel
        {
            Children =
            {
                new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Children = { new TextBlock { Text = "Device" }, deviceCombo },
                },
                new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Children = { new TextBlock { Text = "Wiz action" }, wizKindCombo },
                },
                brightnessPanel,
                colorPanel,
                colorTempPanel,
            },
        };

        if (devices.Count > 0)
        {
            deviceCombo.SelectedIndex = 0;
        }
        RefreshWizKinds();
        // RefreshWizKinds only raises SelectionChanged when the kind changes (never with no
        // devices), so apply the initial visibility explicitly.
        RefreshParameterPanelVisibility();
    }

    /// <summary>The category name in the dialog's picker.</summary>
    public override string DisplayName => "Wiz Device";

    /// <summary>The device and action pickers plus each action kind's parameter panel.</summary>
    public override UIElement View { get; }

    /// <summary>
    /// False with no devices: there's nothing to target, so the dialog preselects another category.
    /// </summary>
    public override bool IsPreferredForNewAction => devices.Count > 0;

    /// <summary>The device picked in the Device combo, if any.</summary>
    private WizDevice? SelectedDevice => deviceCombo.SelectedItem as WizDevice;

    /// <summary>True for every Wiz device action.</summary>
    /// <param name="action">The action being edited.</param>
    public override bool CanEdit(IEventAction action) => action is WizDeviceActionBase;

    /// <summary>Shows an existing action's device, kind and parameters.</summary>
    /// <param name="action">A <see cref="WizDeviceActionBase"/>.</param>
    public override void Load(IEventAction action)
    {
        var wiz = (WizDeviceActionBase)action;
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
    }

    /// <summary>Builds the selected kind of action for the selected device.</summary>
    /// <param name="error">Why no action could be built, when null is returned.</param>
    public override IEventAction? TryBuild(out string? error)
    {
        if (SelectedDevice is not { } device)
        {
            error = "Select a device.";
            return null;
        }
        if (wizKindCombo.SelectedItem is not WizActionKindOption option)
        {
            error = "Select an action.";
            return null;
        }

        error = null;
        return option.Kind switch
        {
            WizTurnOnAction.ActionKind => new WizTurnOnAction { DeviceId = device.Id },
            WizTurnOffAction.ActionKind => new WizTurnOffAction { DeviceId = device.Id },
            WizToggleAction.ActionKind => new WizToggleAction { DeviceId = device.Id },
            WizSetBrightnessAction.ActionKind => new WizSetBrightnessAction { DeviceId = device.Id, Brightness = (int)brightnessSlider.Value },
            WizSetColorAction.ActionKind => new WizSetColorAction { DeviceId = device.Id, R = (byte)rSlider.Value, G = (byte)gSlider.Value, B = (byte)bSlider.Value },
            WizSetColorTemperatureAction.ActionKind => new WizSetColorTemperatureAction { DeviceId = device.Id, ColorTemperatureKelvin = (int)colorTempSlider.Value },
            _ => throw new InvalidOperationException($"Unknown action kind \"{option.Kind}\"."),
        };
    }

    /// <summary>
    /// Offers only the action kinds the selected device supports, keeping the current kind when
    /// it's still offered.
    /// </summary>
    private void RefreshWizKinds()
    {
        var offered = WizActionKinds.For(SelectedDevice);
        var kinds = AllWizKinds.Where(option => offered.Contains(option.Kind)).ToArray();

        var previouslySelected = wizKindCombo.SelectedItem as WizActionKindOption;
        wizKindCombo.ItemsSource = kinds;
        wizKindCombo.SelectedItem = previouslySelected is { } option && kinds.Contains(option)
            ? option
            : kinds.FirstOrDefault();
    }

    /// <summary>Shows only the parameter panel the selected action kind uses.</summary>
    private void RefreshParameterPanelVisibility()
    {
        var kind = (wizKindCombo.SelectedItem as WizActionKindOption)?.Kind;
        brightnessPanel.Visibility = kind == SetBrightnessOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorPanel.Visibility = kind == SetColorOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        colorTempPanel.Visibility = kind == SetColorTemperatureOption.Kind ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Previews the color the R/G/B sliders currently describe.</summary>
    private void UpdateColorSwatch() =>
        colorSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value));
}
