namespace LifeRPG.Core.Character;

public sealed class CharacterProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = "Adventurer";

    public int Level { get; private set; } = 1;

    public int Experience { get; private set; }

    public CharacterAttributes Attributes { get; } = new();

    public void AddExperience(int amount)
    {
        if (amount <= 0)
            return;

        Experience += amount;
        UpdateLevel();
    }

    private void UpdateLevel()
    {
        Level = Math.Max(1, (Experience / 100) + 1);
    }
}
