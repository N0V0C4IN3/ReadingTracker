namespace ReadingTracker.Gateway;

/// <summary>
/// The names the Gateway needs when it turns a Google token into a reader. Per ADR-0001 this is
/// the only place in the system that knows anything about Google at all.
/// </summary>
public static class GoogleIdentity
{
    public const string Issuer = "https://accounts.google.com";

    /// <summary>
    /// Google's stable identifier for a person. Deliberately not their email address: an email
    /// can be renamed or handed to somebody else, and a reader's library must not follow it.
    ///
    /// This is what the services behind the Gateway currently receive as their reader id. When
    /// the Identity service introduces internal ids, this is the single place that changes,
    /// because the header is an opaque string to everything downstream.
    /// </summary>
    public const string SubjectClaim = "sub";

    /// <summary>
    /// The header the services behind the Gateway trust (ADR-0007). Its name is duplicated in
    /// Library rather than shared through a project reference: it is a wire contract between two
    /// independently deployed services, not shared code.
    /// </summary>
    public const string ReaderHeader = "X-Reader-Id";
}
