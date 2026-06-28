using System.Linq;
using BCUKCompanion.Core.Models;

namespace BCUKCompanion.ExpiredMinotaur.Wiz;

public sealed record WizActionResult(WizAction Action, bool Success, string? ErrorMessage);

public sealed record EventDispatchResult(string RewardTitle, IReadOnlyList<WizActionResult> ActionResults)
{
    public bool AllSucceeded => ActionResults.Count > 0 && ActionResults.All(r => r.Success);
}

public sealed class EventActionDispatcher(Func<WizConfig> configProvider, WizClient client)
{
    // Core contract values from BCUKCompanion.Core.Models.BotEventArgs, not arbitrary choices.
    private const string RedemptionEventName = "redemption.received";
    private const string RewardTitleMetadataKey = "rewardTitle";

    public Task<EventDispatchResult?> DispatchAsync(BotEventArgs botEvent, CancellationToken cancellationToken = default)
    {
        if (botEvent.EventName != RedemptionEventName
            || !botEvent.Metadata.TryGetValue(RewardTitleMetadataKey, out var rewardTitle)
            || string.IsNullOrEmpty(rewardTitle))
        {
            return Task.FromResult<EventDispatchResult?>(null);
        }

        return DispatchAsync(rewardTitle, cancellationToken).ContinueWith(
            t => (EventDispatchResult?)t.Result, cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public async Task<EventDispatchResult> DispatchAsync(string rewardTitle, CancellationToken cancellationToken = default)
    {
        var config = configProvider();

        var actions = config.Mappings
            .Where(m => string.Equals(m.RewardTitle, rewardTitle, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => m.Actions)
            .ToList();

        var results = await Task.WhenAll(actions.Select(action => ExecuteActionAsync(action, config, cancellationToken)))
            .ConfigureAwait(false);

        return new EventDispatchResult(rewardTitle, results);
    }

    private async Task<WizActionResult> ExecuteActionAsync(WizAction action, WizConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var device = config.Devices.FirstOrDefault(d => d.Id == action.DeviceId);
            var errors = WizAction.Validate(action, device);
            if (errors.Count > 0)
            {
                return new WizActionResult(action, false, string.Join("; ", errors));
            }

            var success = await SendActionAsync(action, device!, cancellationToken).ConfigureAwait(false);
            return new WizActionResult(action, success, success ? null : "Device did not acknowledge the command.");
        }
        catch (Exception ex)
        {
            return new WizActionResult(action, false, ex.Message);
        }
    }

    private async Task<bool> SendActionAsync(WizAction action, WizDevice device, CancellationToken cancellationToken)
    {
        switch (action.ActionKind)
        {
            case WizActionKind.TurnOn:
                return await client.SetPilotAsync(device.IpAddress, new { state = true }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            case WizActionKind.TurnOff:
                return await client.SetPilotAsync(device.IpAddress, new { state = false }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            case WizActionKind.Toggle:
                var status = await client.GetPilotAsync(device.IpAddress, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (status is null)
                {
                    return false;
                }
                return await client.SetPilotAsync(device.IpAddress, new { state = !status.State }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            case WizActionKind.SetBrightness:
                return await client.SetPilotAsync(
                    device.IpAddress, new { state = true, dimming = action.Brightness }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            case WizActionKind.SetColor:
                return await client.SetPilotAsync(
                    device.IpAddress, new { state = true, r = action.R, g = action.G, b = action.B }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            case WizActionKind.SetColorTemperature:
                return await client.SetPilotAsync(
                    device.IpAddress, new { state = true, temp = action.ColorTemperatureKelvin }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            default:
                return false;
        }
    }
}
