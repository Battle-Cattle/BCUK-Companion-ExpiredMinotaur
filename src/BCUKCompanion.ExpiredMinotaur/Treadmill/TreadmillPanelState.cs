namespace BCUKCompanion.ExpiredMinotaur.Treadmill;

/// <summary>The treadmill panel's display rules, kept out of the WPF panel so they can be tested.</summary>
internal static class TreadmillPanelState
{
    /// <summary>Formats the panel's speed line.</summary>
    /// <param name="currentKmh">The belt's current speed in km/h.</param>
    /// <param name="targetKmh">The speed the treadmill is heading for, in km/h.</param>
    /// <returns>The line, e.g. "Current: 4.5 km/h   Target: 5.0 km/h".</returns>
    public static string FormatSpeed(double currentKmh, double targetKmh) =>
        $"Current: {currentKmh:0.0} km/h   Target: {targetKmh:0.0} km/h";

    /// <summary>Which of the Connect and Disconnect buttons should be enabled.</summary>
    /// <param name="operationInProgress">Whether a connect or disconnect is still running.</param>
    /// <param name="isConnected">Whether the treadmill is connected.</param>
    /// <returns>
    /// Neither while an operation is running (so a second click can't queue behind it);
    /// otherwise Connect when disconnected and Disconnect when connected.
    /// </returns>
    public static (bool Connect, bool Disconnect) ConnectionButtons(bool operationInProgress, bool isConnected) =>
        operationInProgress ? (false, false) : (!isConnected, isConnected);
}
