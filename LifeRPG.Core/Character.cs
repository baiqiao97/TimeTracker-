namespace LifeRPG.Core;

/// <summary>
/// Represents the player's RPG profile built from real-life activities.
/// This layer is intentionally independent from TimeTracker UI and storage.
/// </summary>
public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Traveler";

    public int Level { get; private set; } = 1;

    public long Experience { get; private set; }

    public Attributes Attributes { get; set; } = new();

    public void GainExperience(long amount)
    {
        if (amount <= 0) return;
        Experience += amount;
        UpdateLevel();
    }

    private void UpdateLevel()
    {
        Level = (int)Math.Floor(Math.Sqrt(Experience / 100.0)) + 1;
    }
}
