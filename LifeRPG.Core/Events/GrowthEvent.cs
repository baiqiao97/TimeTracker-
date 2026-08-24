namespace LifeRPG.Core.Events;

/// <summary>
/// Represents a real-world action that can contribute to character growth.
/// </summary>
public sealed class GrowthEvent
{
    public string Type { get; init; } = string.Empty;

    public int DurationMinutes { get; init; }

    public string Source { get; init; } = "Unknown";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
