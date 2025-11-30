namespace ConsoleChat.Models
{
    /// <summary>
    /// Типы сообщений в протоколе
    /// </summary>
    public enum MessageType
    {
        Text,
        Join,
        KeyExchange,
        PeerList,
        Ban,
        Unban,
        Ping,
    }

    /// <summary>
    /// Сообщение чата
    /// </summary>
    public sealed class ChatMessage
    {
        public required string Id { get; init; }
        public required string SenderId { get; init; }
        public required string SenderName { get; init; }
        public required MessageType Type { get; init; }
        public required string Content { get; init; }
        public string? EncryptedContent { get; set; }
        public string? EncryptedAesKey { get; set; }
        public string? Iv { get; set; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? TargetUserId { get; set; }

        public static ChatMessage CreateText(string senderId, string senderName, string content) => new()
        {
            Id = Guid.NewGuid().ToString(),
            SenderId = senderId,
            SenderName = senderName,
            Type = MessageType.Text,
            Content = content
        };

        public static ChatMessage CreateSystem(MessageType type, string senderId, string senderName, string content) => new()
        {
            Id = Guid.NewGuid().ToString(),
            SenderId = senderId,
            SenderName = senderName,
            Type = type,
            Content = content
        };
    }
}
