namespace LifeRPG.Core.Character;

public sealed class CharacterAttributes
{
    public int Intelligence { get; private set; }

    public int Discipline { get; private set; }

    public int Fitness { get; private set; }

    public int Creativity { get; private set; }

    public int Social { get; private set; }

    public void Increase(string attribute, int amount)
    {
        if (amount <= 0)
            return;

        switch (attribute.ToLowerInvariant())
        {
            case "intelligence":
                Intelligence += amount;
                break;
            case "discipline":
                Discipline += amount;
                break;
            case "fitness":
                Fitness += amount;
                break;
            case "creativity":
                Creativity += amount;
                break;
            case "social":
                Social += amount;
                break;
        }
    }
}
