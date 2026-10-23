using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

/// <summary>Adds a WiZ device by hand, or edits an existing one: name, IP address, type and capabilities.</summary>
public sealed class WizDeviceEditDialog : Window
{
    private readonly TextBox nameBox = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox ipBox = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox deviceTypeCombo = new() { ItemsSource = Enum.GetValues<WizDeviceType>(), Margin = new Thickness(0, 0, 0, 8) };
    private readonly CheckBox colorCheck = new() { Content = "Supports color", Margin = new Thickness(0, 0, 0, 4) };
    private readonly CheckBox colorTempCheck = new() { Content = "Supports color temperature", Margin = new Thickness(0, 0, 0, 4) };
    private readonly CheckBox dimmingCheck = new() { Content = "Supports dimming", Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock errorText = new() { Foreground = System.Windows.Media.Brushes.Red, TextWrapping = TextWrapping.Wrap };

    private readonly Guid id;

    /// <summary>The device as entered, once the dialog closes with OK.</summary>
    public WizDevice? Result { get; private set; }

    /// <summary>Creates the dialog, filled in from <paramref name="existing"/> when editing.</summary>
    /// <param name="existing">The device to edit, or null to add a new one.</param>
    public WizDeviceEditDialog(WizDevice? existing = null)
    {
        id = existing?.Id ?? Guid.NewGuid();

        Title = existing is null ? "Add Device" : "Edit Device";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        nameBox.Text = existing?.Name ?? string.Empty;
        ipBox.Text = existing?.IpAddress ?? string.Empty;
        deviceTypeCombo.SelectedItem = existing?.DeviceType ?? WizDeviceType.Light;
        colorCheck.IsChecked = existing?.SupportsColor ?? false;
        colorTempCheck.IsChecked = existing?.SupportsColorTemperature ?? false;
        dimmingCheck.IsChecked = existing?.SupportsDimming ?? true;

        var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        okButton.Click += (_, _) => OnOk();

        // IsCancel already closes the modal dialog with DialogResult = false (Esc too).
        var cancelButton = new Button { Content = "Cancel", Width = 80, IsCancel = true };

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
                new TextBlock { Text = "Name" },
                nameBox,
                new TextBlock { Text = "IP Address" },
                ipBox,
                new TextBlock { Text = "Device Type" },
                deviceTypeCombo,
                colorCheck,
                colorTempCheck,
                dimmingCheck,
                errorText,
                buttonPanel,
            },
        };
    }

    /// <summary>Closes the dialog with the entered device in <see cref="Result"/>, or shows why the input can't be used.</summary>
    private void OnOk()
    {
        var name = nameBox.Text.Trim();
        var ip = ipBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            errorText.Text = "Name is required.";
            return;
        }

        if (!WizAddress.IsValid(ip))
        {
            errorText.Text = "Enter a valid IP address.";
            return;
        }

        Result = new WizDevice(
            id,
            name,
            ip,
            (WizDeviceType)deviceTypeCombo.SelectedItem!,
            colorCheck.IsChecked ?? false,
            colorTempCheck.IsChecked ?? false,
            dimmingCheck.IsChecked ?? false);
        DialogResult = true;
    }
}
