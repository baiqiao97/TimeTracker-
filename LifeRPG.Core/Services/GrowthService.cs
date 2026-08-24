using LifeRPG.Core.Events;
using LifeRPG.Core.Rules;

namespace LifeRPG.Core.Services;

public sealed class GrowthService
{
    private readonly IReadOnlyCollection<GrowthRule> _rules;

    public GrowthService(IEnumerable<GrowthRule> rules)
    {
        _rules = rules.ToList();
    }

    public GrowthResult? Calculate(GrowthEvent growthEvent)
    {
        var rule = _rules.FirstOrDefault(x =>
            string.Equals(x.EventType, growthEvent.Type, StringComparison.OrdinalIgnoreCase));

        if (rule is null || growthEvent.DurationMinutes <= 0)
            return null;

        var hours = growthEvent.DurationMinutes / 60.0;

        return new GrowthResult
        {
            ExperienceGained = (int)Math.Floor(hours * rule.ExperiencePerHour),
            AttributeName = rule.AttributeName,
            AttributeGained = (int)Math.Floor(hours * rule.AttributeGainPerHour)
        };
    }
}
