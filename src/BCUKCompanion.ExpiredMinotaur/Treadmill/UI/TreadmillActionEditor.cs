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

    /// <summary>Creates the editor with a default delta of 1.0 km/h.</summary>
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

    /// <summary>The category name in the dialog's picker.</summary>
    public override string DisplayName => "Treadmill";

    /// <summary>The speed-delta text box and its label.</summary>
    public override UIElement View { get; }

    /// <summary>True for <see cref="TreadmillNudgeSpeedAction"/>.</summary>
    /// <param name="action">The action being edited.</param>
    /// <returns>True if <paramref name="action"/> is a <see cref="TreadmillNudgeSpeedAction"/>; otherwise false.</returns>
    public override bool CanEdit(IEventAction action) => action is TreadmillNudgeSpeedAction;

    /// <summary>Shows an existing action's delta.</summary>
    /// <param name="action">A <see cref="TreadmillNudgeSpeedAction"/>.</param>
    public override void Load(IEventAction action) =>
        deltaBox.Text = ((TreadmillNudgeSpeedAction)action).DeltaKmh.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Builds a NudgeSpeed action from the delta, which must be a finite number.</summary>
    /// <param name="error">Why the input can't be used, when null is returned.</param>
    /// <returns>A new action with the entered delta, or null if the delta isn't a finite number.</returns>
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
