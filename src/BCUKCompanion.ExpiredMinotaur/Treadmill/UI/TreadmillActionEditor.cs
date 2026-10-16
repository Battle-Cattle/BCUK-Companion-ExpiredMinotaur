using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.TrayApp.Actions;
using TextBox = System.Windows.Controls.TextBox;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.UI;

/// <summary>The "Treadmill" category of the Add/Edit Action dialog: a NudgeSpeed delta.</summary>
public sealed class TreadmillActionEditor : EventActionEditor
{
    private readonly TextBox deltaBox = new() { Text = "1.0" };

    public TreadmillActionEditor()
    {
        View = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "Speed delta (km/h, negative to slow down)" },
                deltaBox,
            },
        };
    }

    public override string DisplayName => "Treadmill";

    public override UIElement View { get; }

    public override bool CanEdit(IEventAction action) => action is TreadmillNudgeSpeedAction;

    public override void Load(IEventAction action) =>
        deltaBox.Text = ((TreadmillNudgeSpeedAction)action).DeltaKmh.ToString("R", CultureInfo.InvariantCulture);

    public override IEventAction? TryBuild(out string? error)
    {
        if (!double.TryParse(deltaBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var delta)
            || !double.IsFinite(delta))
        {
            error = "Enter a numeric speed delta.";
            return null;
        }

        error = null;
        return new TreadmillNudgeSpeedAction { DeltaKmh = delta };
    }
}
