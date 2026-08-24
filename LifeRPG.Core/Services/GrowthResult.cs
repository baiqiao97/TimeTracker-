namespace LifeRPG.Core.Services;

public sealed class GrowthResult
{
    public int ExperienceGained { get; init; }

    public string AttributeName { get; init; } = string.Empty;

    public int AttributeGained { get; init; }
}
