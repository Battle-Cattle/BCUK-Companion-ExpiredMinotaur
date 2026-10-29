using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz.UI;

public sealed class WizActionEditorTests
{
    private static readonly WizDevice ColorLight = new(Guid.NewGuid(), "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);
    private static readonly WizDevice Plug = new(Guid.NewGuid(), "Fan", "192.168.1.22", WizDeviceType.Plug, false, false, false);

    public static TheoryData<IEventAction> Actions() =>
    [
        new WizTurnOnAction { DeviceId = ColorLight.Id },
        new WizTurnOffAction { DeviceId = ColorLight.Id },
        new WizToggleAction { DeviceId = Plug.Id },
        new WizSetBrightnessAction { DeviceId = ColorLight.Id, Brightness = 40 },
        new WizSetColorAction { DeviceId = ColorLight.Id, R = 10, G = 20, B = 30 },
        new WizSetColorTemperatureAction { DeviceId = ColorLight.Id, ColorTemperatureKelvin = 3000 },
    ];

    [SkippableTheory]
    [MemberData(nameof(Actions))]
    public void LoadThenTryBuild_RoundTripsTheAction(IEventAction action)
    {
        WindowsOnly.Skip();
        WpfTest.Run(() => RoundTrip(action));
    }

    private static void RoundTrip(IEventAction action)
    {
        var editor = new WizActionEditor([ColorLight, Plug]);

        Assert.True(editor.CanEdit(action));
        editor.Load(action);
        var built = editor.TryBuild(out var error);

        Assert.Null(error);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize<object>(action), System.Text.Json.JsonSerializer.Serialize<object>(built!));
    }

    [SkippableFact]
    public void WithNoDevices_AsksForOneAndIsntPreferred()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new WizActionEditor([]);

            Assert.Null(editor.TryBuild(out var error));
            Assert.Equal("Select a device.", error);
            Assert.False(editor.IsPreferredForNewAction);
            Assert.Equal("Wiz Device", editor.DisplayName);
            Assert.Equal("Wiz Device", editor.ToString());
        });
    }

    [SkippableFact]
    public void PreselectsTheFirstDeviceAndTurnOn()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new WizActionEditor([ColorLight]);

            Assert.True(editor.IsPreferredForNewAction);
            var built = Assert.IsType<WizTurnOnAction>(editor.TryBuild(out _));
            Assert.Equal(ColorLight.Id, built.DeviceId);
            Assert.False(editor.CanEdit(new DelayAction()));
        });
    }

    [SkippableFact]
    public void PickingAPlug_OffersOnlyOnOffKinds_AndKeepsAKindStillOffered()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new WizActionEditor([ColorLight, Plug]);
            var (deviceCombo, kindCombo) = Combos(editor);

            kindCombo.SelectedIndex = 1; // TurnOff
            deviceCombo.SelectedItem = Plug;

            Assert.Equal(["TurnOn", "TurnOff", "Toggle"], kindCombo.Items.Cast<object>().Select(o => o.ToString()));
            Assert.Equal("TurnOff", kindCombo.SelectedItem!.ToString());
        });
    }

    [SkippableFact]
    public void WithNoKindSelected_AsksForOne()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new WizActionEditor([ColorLight]);
            Combos(editor).Kind.SelectedItem = null;

            Assert.Null(editor.TryBuild(out var error));
            Assert.Equal("Select an action.", error);
        });
    }

    [SkippableFact]
    public void ShowsOnlyTheSelectedKindsParameters_AndPreviewsThem()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var editor = new WizActionEditor([ColorLight]);
            editor.Load(new WizSetColorAction { DeviceId = ColorLight.Id, R = 1, G = 2, B = 3 });

            var panels = WpfTest.All<StackPanel>(editor.View).Where(p => p.Margin.Bottom == 8 && p.Children.Count > 1 && p.Children[0] is TextBlock).ToList();
            var visible = panels.Where(p => p.Visibility == Visibility.Visible).Select(p => ((TextBlock)p.Children[0]).Text);
            Assert.Contains("Color", visible);
            Assert.DoesNotContain("Brightness", visible);
            var swatch = WpfTest.All<Border>(editor.View).Single();
            Assert.Equal(Color.FromRgb(1, 2, 3), ((SolidColorBrush)swatch.Background).Color);

            editor.Load(new WizSetBrightnessAction { DeviceId = ColorLight.Id, Brightness = 55 });
            Assert.Contains("55%", WpfTest.Texts(editor.View));

            editor.Load(new WizSetColorTemperatureAction { DeviceId = ColorLight.Id, ColorTemperatureKelvin = 2700 });
            Assert.Contains("2700K", WpfTest.Texts(editor.View));
        });
    }

    private static (ComboBox Device, ComboBox Kind) Combos(WizActionEditor editor)
    {
        var combos = WpfTest.All<ComboBox>(editor.View).ToList();
        return (combos[0], combos[1]);
    }
}
