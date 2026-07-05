using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.UI;

public sealed class TreadmillActionEditDialog : Window
{
    private sealed record TreadmillActionKindOption(string Kind, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private static readonly TreadmillActionKindOption NudgeSpeedOption = new(TreadmillNudgeSpeedAction.ActionKind, "NudgeSpeed");
    private static readonly TreadmillActionKindOption DelayOption = new(DelayAction.ActionKind, "Delay");
    private static readonly TreadmillActionKindOption[] AllKinds = [NudgeSpeedOption, DelayOption];

    private readonly TreadmillActionContext context;
    private readonly ComboBox actionKindCombo = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock errorText = new() { Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    private readonly StackPanel deltaPanel;
    private readonly TextBox deltaBox = new() { Text = "1.0" };

    private readonly StackPanel delayPanel;
    private readonly TextBox delayBox = new() { Text = "5" };

    public IEventAction? Result { get; private set; }

    public TreadmillActionEditDialog(TreadmillClient client, IEventAction? existing = null)
    {
        context = new TreadmillActionContext(client);

        Title = existing is null ? "Add Action" : "Edit Action";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        actionKindCombo.ItemsSource = AllKinds;
        actionKindCombo.SelectionChanged += (_, _) => RefreshParameterPanelVisibility();

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
                new TextBlock { Text = "Action" },
                actionKindCombo,
                deltaPanel,
                delayPanel,
                errorText,
                buttonPanel,
            },
        };

        if (existing is not null)
        {
            actionKindCombo.SelectedItem = AllKinds.FirstOrDefault(o => o.Kind == existing.Kind);

            switch (existing)
            {
                case TreadmillNudgeSpeedAction nudge:
                    deltaBox.Text = nudge.DeltaKmh.ToString("0.0");
                    break;
                case DelayAction delay:
                    delayBox.Text = delay.DelaySeconds.ToString();
                    break;
            }
        }
        else
        {
            actionKindCombo.SelectedItem = NudgeSpeedOption;
        }

        RefreshParameterPanelVisibility();
    }

    private void RefreshParameterPanelVisibility()
    {
        var kind = (actionKindCombo.SelectedItem as TreadmillActionKindOption)?.Kind;
        deltaPanel.Visibility = kind == NudgeSpeedOption.Kind ? Visibility.Visible : Visibility.Collapsed;
        delayPanel.Visibility = kind == DelayOption.Kind ? Visibility.Visible : Visibility.Collapsed;
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
        if (actionKindCombo.SelectedItem is not TreadmillActionKindOption option)
            return (null, "Select an action.");

        return option.Kind == DelayAction.ActionKind ? BuildDelayAction() : BuildNudgeSpeedAction();
    }

    private (IEventAction?, string?) BuildDelayAction()
    {
        if (!int.TryParse(delayBox.Text.Trim(), out var seconds))
            return (null, "Enter a whole number of seconds.");
        var action = new DelayAction { DelaySeconds = seconds };
        var errors = action.Validate(context);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }

    private (IEventAction?, string?) BuildNudgeSpeedAction()
    {
        if (!double.TryParse(deltaBox.Text.Trim(), out var delta))
            return (null, "Enter a numeric speed delta.");
        var action = new TreadmillNudgeSpeedAction { DeltaKmh = delta };
        var errors = action.Validate(context);
        return errors.Count > 0 ? (null, string.Join("\n", errors)) : (action, null);
    }
}
