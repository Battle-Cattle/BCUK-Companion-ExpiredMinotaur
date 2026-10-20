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
    /// <summary>The <c>kind</c> this action is stored under in actions-config.json.</summary>
    public const string ActionKind = "treadmill.nudgeSpeed";

    /// <summary>How much to change the target speed by, in km/h; negative slows down.</summary>
    public double DeltaKmh { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string Kind => ActionKind;

    /// <summary>Describes the action for the mappings list, e.g. "Speed up by 1.0 km/h".</summary>
    /// <param name="context">Unused: the description needs no live state.</param>
    public string Describe(IEventActionContext context) => DeltaKmh >= 0
        ? $"Speed up by {DeltaKmh:0.0} km/h"
        : $"Slow down by {Math.Abs(DeltaKmh):0.0} km/h";

    // Connection state is deliberately not checked here — the treadmill is manually
    // connected (see TreadmillClient), so mappings must be configurable while it's off or
    // unpaired. ExecuteAsync/NudgeSpeedAsync already fail gracefully (return false, not
    // throw) when disconnected, which is enough for the dispatcher to report the action as
    // unsuccessful without blocking edits made while disconnected.
    /// <summary>Rejects a zero delta, which would do nothing.</summary>
    /// <param name="context">Unused: see the comment above.</param>
    public IReadOnlyList<string> Validate(IEventActionContext context)
        => DeltaKmh == 0 ? ["Delta must be non-zero."] : [];

    /// <summary>Nudges the connected treadmill's target speed by <see cref="DeltaKmh"/>.</summary>
    /// <param name="context">Must provide a <see cref="TreadmillActionContext"/>.</param>
    /// <param name="cancellationToken">Cancels the nudge.</param>
    /// <returns>Whether the treadmill accepted the new target.</returns>
    /// <exception cref="InvalidOperationException">The context has no <see cref="TreadmillActionContext"/>.</exception>
    public async Task<bool> ExecuteAsync(IEventActionContext context, CancellationToken cancellationToken)
    {
        var client = context.GetService<TreadmillActionContext>()?.Client
            ?? throw new InvalidOperationException("Treadmill action context not available.");

        return await client.NudgeSpeedAsync(DeltaKmh, cancellationToken).ConfigureAwait(false);
    }
}
