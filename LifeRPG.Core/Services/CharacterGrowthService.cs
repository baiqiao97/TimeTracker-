using LifeRPG.Core.Character;
using LifeRPG.Core.Events;
using LifeRPG.Core.Rules;

namespace LifeRPG.Core.Services;

public sealed class CharacterGrowthService
{
    private readonly GrowthService _growthService;

    public CharacterGrowthService(IEnumerable<GrowthRule> rules)
    {
        _growthService = new GrowthService(rules);
    }

    public GrowthResult? Apply(CharacterProfile character, GrowthEvent growthEvent)
    {
        var result = _growthService.Calculate(growthEvent);

        if (result is null)
            return null;

        character.AddExperience(result.ExperienceGained);
        character.Attributes.Increase(result.AttributeName, result.AttributeGained);

        return result;
    }
}
