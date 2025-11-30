namespace ConsoleChat.Models
{
    /// <summary>
    /// Роли пользователей в чате
    /// </summary>
    public enum UserRole
    {
        User,
        Admin
    }

    /// <summary>
    /// Модель пользователя чата
    /// </summary>
    public sealed class User
    {
        public required string Id { get; init; }
        public required string Username { get; init; }
        public required string PublicKey { get; init; }
        public required string Endpoint { get; init; }
        public UserRole Role { get; set; } = UserRole.User;
        public bool IsBanned { get; set; }
        public DateTime JoinedAt { get; init; } = DateTime.UtcNow;

        public override string ToString() => $"{Username} ({Role})";

        public override bool Equals(object? obj) => obj is User user && Id == user.Id;

        public override int GetHashCode() => Id.GetHashCode();
    }
}
