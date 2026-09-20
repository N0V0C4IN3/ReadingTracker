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

    private DeviceToken()
    {
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
