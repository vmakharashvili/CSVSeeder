namespace CSVSeeder.API.Models;

public class User
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<TodoItem> TodoItems { get; set; } = new HashSet<TodoItem>();
}
