namespace LifeRPG.Core.Rules;

/// <summary>
/// Defines how an event transforms into character growth.
/// </summary>
public sealed class GrowthRule
{
    public string EventType { get; init; } = string.Empty;

    public int ExperiencePerHour { get; init; }

    public string AttributeName { get; init; } = string.Empty;

    public int AttributeGainPerHour { get; init; }
}
