namespace TimeTracker
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#3498db";
        public string Description { get; set; } = string.Empty;

        public Category(string name, string color = "#3498db", string description = "")
        {
            Name = name;
            Color = color;
            Description = description;
        }

        public Category() { }
    }
}