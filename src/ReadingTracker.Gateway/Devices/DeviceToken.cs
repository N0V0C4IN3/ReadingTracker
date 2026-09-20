namespace ReadingTracker.Gateway.Devices;

/// <summary>
/// A long-lived secret a Reader minted for one device that cannot sign in with Google. What is
/// kept here is everything about the token except the token: the secret itself is handed to the
/// Reader once, at minting, and only its hash is stored, so a copy of this table is not a copy
/// of anyone's device.
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; private set; }

    /// <summary>The reader this token acts as — the same subject a Google token would carry.</summary>
    public string ReaderId { get; private set; } = "";

    /// <summary>What the Reader called the device, so they can tell their tokens apart.</summary>
    public string Name { get; private set; } = "";

    /// <summary>A hash of the secret, never the secret. What a presented token is looked up by.</summary>
    public string SecretHash { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null until the token has been used at all.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// How stale a last-used stamp may be before a use refreshes it. A Kindle turning pages must
    /// not turn every request into a write; "last used within the minute" is as much as a Reader
    /// looking at the Devices page will ever want to know.
    /// </summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    private DeviceToken()
    {
    }

    /// <summary>Notes a use; says whether anything changed and so needs saving.</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (LastUsedAt is { } lastUsed && now - lastUsed < LastUsedResolution)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    public static DeviceToken Mint(string readerId, string name, string secretHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            ReaderId = readerId,
            Name = name,
            SecretHash = secretHash,
            CreatedAt = now,
        };
}
