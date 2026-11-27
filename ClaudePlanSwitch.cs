namespace CodexUsageTray;

/// <summary>
/// Switches the tray between a Claude plan that has a five-hour limit and one that does not.
/// One choice moves the two settings that only make sense with that limit, the Claude 5-hour
/// tray number and the Claude 5-hour phone alert, so changing plan is a single tick.
/// </summary>
internal static class ClaudePlanSwitch
{
    internal const string MenuText = "Claude plan has a 5-hour limit";

    /// <summary>
    /// Reports which plan the tray is set up for. There is no separate saved flag to drift:
    /// the plan has a five-hour limit exactly when the Claude 5-hour tray number is shown.
    /// </summary>
    /// <param name="numbers">The tray numbers the user chose.</param>
    /// <returns><see langword="true"/> when the tray is set up for a plan with a five-hour limit.</returns>
    internal static bool HasFiveHourLimit(TrayNumberStore numbers) =>
        numbers.IsShown(UsageSources.Claude, LimitWindow.FiveHour);

    /// <summary>
    /// Shows or hides the Claude 5-hour tray number and gives the phone alert choices to save with it.
    /// Every other tray number and alert choice is left as it was.
    /// </summary>
    /// <param name="numbers">The tray numbers to change.</param>
    /// <param name="alerts">The current phone alert choices.</param>
    /// <param name="hasFiveHourLimit">Whether the Claude plan has a five-hour limit.</param>
    /// <param name="switched">The phone alert choices for that plan, or the current ones when nothing changed.</param>
    /// <returns>
    /// <see langword="false"/> when the tray number could not be changed, for example because it is
    /// the last one shown. The phone alerts are then left alone as well.
    /// </returns>
    internal static bool TrySwitch(
        TrayNumberStore numbers,
        PhoneNotificationSettings alerts,
        bool hasFiveHourLimit,
        out PhoneNotificationSettings switched)
    {
        switched = alerts;
        if (HasFiveHourLimit(numbers) != hasFiveHourLimit
            && !numbers.TrySet(UsageSources.Claude, LimitWindow.FiveHour, hasFiveHourLimit))
        {
            return false;
        }

        switched = alerts with { ClaudeFiveHour = hasFiveHourLimit };
        return true;
    }
}
