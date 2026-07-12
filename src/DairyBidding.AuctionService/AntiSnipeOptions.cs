namespace DairyBidding.AuctionService;

/// <summary>ADR-034/040: Configuration for the anti-snipe closing extension behaviour.</summary>
public sealed class AntiSnipeOptions
{
    /// <summary>
    /// Bids placed within this many minutes of <c>ends_at</c> trigger an extension.
    /// </summary>
    public int WindowMinutes { get; set; } = 5;

    /// <summary>
    /// Number of minutes to extend <c>ends_at</c> by when triggered.
    /// </summary>
    public int ExtensionMinutes { get; set; } = 5;

    /// <summary>
    /// ADR-040: Maximum number of anti-snipe extensions allowed per auction.
    /// Industry standard (Copart, PropertyGuru): 10.
    /// </summary>
    public int MaxExtensionsPerAuction { get; set; } = 10;
}
