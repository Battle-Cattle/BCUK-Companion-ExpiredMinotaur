using System.Text.Json.Serialization;
using BCUKCompanion.Core.Actions;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;

/// <summary>
/// Nudges the treadmill's target speed by a delta (e.g. "+1 km/h" on a channel-point
/// redemption). There's no Start/Stop kind — the treadmill is expected to already be
/// running before any bot event fires.
/// </summary>
public sealed class TreadmillNudgeSpeedAction : IEventAction
{
    public const string ActionKind = "treadmill.nudgeSpeed";

    public double DeltaKmh { get; set; }

    [JsonIgnore]
    public string Kind => ActionKind;

    public string Describe(IEventActionContext context) => DeltaKmh >= 0
        ? $"Speed up by {DeltaKmh:0.0} km/h"
        : $"Slow down by {Math.Abs(DeltaKmh):0.0} km/h";

    // Connection state is deliberately not checked here — the treadmill is manually
    // connected (see TreadmillClient), so mappings must be configurable while it's off or
    // unpaired. ExecuteAsync/NudgeSpeedAsync already fail gracefully (return false, not
    // throw) when disconnected, which is enough for the dispatcher to report the action as
    // unsuccessful without blocking edits made while disconnected.
    public IReadOnlyList<string> Validate(IEventActionContext context)
        => DeltaKmh == 0 ? ["Delta must be non-zero."] : [];

    public async Task<bool> ExecuteAsync(IEventActionContext context, CancellationToken cancellationToken)
    {
        var client = context.GetService<TreadmillActionContext>()?.Client
            ?? throw new InvalidOperationException("Treadmill action context not available.");

        return await client.NudgeSpeedAsync(DeltaKmh, cancellationToken).ConfigureAwait(false);
    }
}
