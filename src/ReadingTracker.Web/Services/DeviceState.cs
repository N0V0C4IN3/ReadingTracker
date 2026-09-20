namespace ReadingTracker.Web.Services;

public enum DeviceStateKind
{
    /// <summary>Used within the last day: the plugin on it is talking to ReadingTracker.</summary>
    Syncing,
    /// <summary>Not heard from for a day or more. Long enough, and it is the one that was lost.</summary>
    Quiet,
    /// <summary>Minted, and the token never presented — a set-up that was never finished.</summary>
    NeverUsed,
}

/// <summary>
/// What a device is doing, read off when its token was last used. The question a reader brings
/// to the Devices page is "is my Kindle still talking to this?", and a date alone makes them do
/// the sum; this does it for them, and says it in a word.
/// </summary>
public sealed record DeviceState(DeviceStateKind Kind, string Label)
{
    public static DeviceState Of(Device device, DateTimeOffset now)
    {
        if (device.LastUsedAt is not { } used)
        {
            return new(DeviceStateKind.NeverUsed, "Never used");
        }

        // The Gateway coarsens LastUsedAt to the minute, and the reader's clock may run a little
        // ahead of it; neither makes a device that reported just now anything but syncing.
        var since = now - used;
        if (since < TimeSpan.FromDays(1))
        {
            return new(DeviceStateKind.Syncing, "Syncing");
        }

        var days = (int)Math.Floor(since.TotalDays);
        return new(DeviceStateKind.Quiet, days == 1 ? "Quiet 1 day" : $"Quiet {days} days");
    }
}
