namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public enum WizDeviceType
{
    Light,
    Plug,
}

public enum WizActionKind
{
    TurnOn,
    TurnOff,
    Toggle,
    SetBrightness,
    SetColor,
    SetColorTemperature,
}

public sealed record WizDevice(
    Guid Id,
    string Name,
    string IpAddress,
    WizDeviceType DeviceType,
    bool SupportsColor,
    bool SupportsColorTemperature,
    bool SupportsDimming)
{
    public override string ToString() => $"{Name} ({IpAddress}) - {DeviceType}";
}

public sealed record WizAction(
    Guid DeviceId,
    WizActionKind ActionKind,
    int? Brightness = null,
    byte? R = null,
    byte? G = null,
    byte? B = null,
    int? ColorTemperatureKelvin = null)
{
    public const int MinBrightness = 1;
    public const int MaxBrightness = 100;
    public const int MinColorTemperatureKelvin = 2200;
    public const int MaxColorTemperatureKelvin = 6500;

    public static IReadOnlyList<string> Validate(WizAction action, WizDevice? device)
    {
        var errors = new List<string>();

        if (device is null)
        {
            errors.Add("Device not found.");
            return errors;
        }

        if (device.DeviceType == WizDeviceType.Plug
            && action.ActionKind is not (WizActionKind.TurnOn or WizActionKind.TurnOff or WizActionKind.Toggle))
        {
            errors.Add($"Plugs do not support {action.ActionKind}.");
        }

        switch (action.ActionKind)
        {
            case WizActionKind.SetBrightness:
                if (!device.SupportsDimming)
                {
                    errors.Add($"{device.Name} does not support brightness control.");
                }
                if (action.Brightness is null || action.Brightness < MinBrightness || action.Brightness > MaxBrightness)
                {
                    errors.Add($"Brightness must be between {MinBrightness} and {MaxBrightness}.");
                }
                break;

            case WizActionKind.SetColor:
                if (!device.SupportsColor)
                {
                    errors.Add($"{device.Name} does not support color.");
                }
                if (action.R is null || action.G is null || action.B is null)
                {
                    errors.Add("Color requires R, G, and B values.");
                }
                break;

            case WizActionKind.SetColorTemperature:
                if (!device.SupportsColorTemperature)
                {
                    errors.Add($"{device.Name} does not support color temperature.");
                }
                if (action.ColorTemperatureKelvin is null
                    || action.ColorTemperatureKelvin < MinColorTemperatureKelvin
                    || action.ColorTemperatureKelvin > MaxColorTemperatureKelvin)
                {
                    errors.Add($"Color temperature must be between {MinColorTemperatureKelvin}K and {MaxColorTemperatureKelvin}K.");
                }
                break;
        }

        return errors;
    }
}

public sealed record EventActionMapping(string RewardTitle, List<WizAction> Actions)
{
    public override string ToString() => $"{RewardTitle} ({Actions.Count} action(s))";
}

public sealed record WizConfig
{
    public List<WizDevice> Devices { get; init; } = [];
    public List<EventActionMapping> Mappings { get; init; } = [];
}
