using ConsoleChat.Cryptography;
using ConsoleChat.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleChat.Network
{
    /// <summary>
    /// P2P узел - основа децентрализованной сети
    /// Каждый узел может принимать входящие и устанавливать исходящие соединения
    /// </summary>
    public sealed class P2PNode : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
        private readonly ConcurrentDictionary<string, User> _users = new();
        private readonly HashSet<string> _bannedUsers = new();
        private readonly CryptoService _crypto;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _banLock = new();

        private byte[] _sharedChatKey;

        public User LocalUser { get; }
        public int Port { get; }
        public IReadOnlyDictionary<string, User> Users => _users;
        public IReadOnlyCollection<string> BannedUserIds
        {
            get { lock (_banLock) return _bannedUsers.ToList(); }
        }

        public event Action<ChatMessage>? MessageReceived;
        public event Action<User>? UserJoined;
        public event Action<User>? UserLeft;
        public event Action<string>? SystemMessage;

        public P2PNode(string username, int port, bool isAdmin = false)
        {
            Port = port;
            _crypto = new CryptoService();

            // Генерируем общий ключ чата (в реальной системе это был бы distributed key exchange)
            _sharedChatKey = new byte[32];
            Random.Shared.NextBytes(_sharedChatKey);

            LocalUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                PublicKey = _crypto.PublicKey,
                Endpoint = $"127.0.0.1:{port}",
                Role = isAdmin ? UserRole.Admin : UserRole.User
            };

            _users[LocalUser.Id] = LocalUser;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        /// <summary>
        /// Запускает узел (начинает прослушивание входящих соединений)
        /// </summary>
        public async Task StartAsync()
        {
            _listener.Start();
            SystemMessage?.Invoke($"🚀 Узел запущен на порту {Port}");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    var connection = new PeerConnection(client, endpoint);

                    SetupConnectionHandlers(connection);
                    connection.StartReceiving();
                }
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое завершение
            }
        }

        /// <summary>
        /// Подключается к другому пиру
        /// </summary>
        public async Task<bool> ConnectToPeerAsync(string host, int port)
        {
            try
            {
                var endpoint = $"{host}:{port}";

                // Проверяем, не подключены ли мы уже
                if (_peers.Values.Any(p => p.PeerEndpoint == endpoint || p.PeerId != string.Empty))
                {
                    SystemMessage?.Invoke($"⚠️ Уже подключены к {endpoint}");
                    return false;
                }

                var client = new TcpClient();
                await client.ConnectAsync(host, port);

                var connection = new PeerConnection(client, endpoint);
                SetupConnectionHandlers(connection);
                connection.StartReceiving();

                await SendJoinMessageAsync(connection);

                SystemMessage?.Invoke($"✅ Подключились к {endpoint}");
                return true;
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"❌ Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отправляет текстовое сообщение всем пирам (broadcast с шифрованием)
        /// </summary>
        public async Task BroadcastMessageAsync(string content)
        {
            var encrypted = _crypto.EncryptBroadcast(content, _sharedChatKey);

            var message = ChatMessage.CreateText(LocalUser.Id, LocalUser.Username, "[encrypted]");
            message.EncryptedContent = encrypted.EncryptedContent;
            message.Iv = encrypted.Iv;

            await BroadcastAsync(message);

            // Показываем себе
            MessageReceived?.Invoke(ChatMessage.CreateText(LocalUser.Id, LocalUser.Username, content));
        }

        /// <summary>
        /// Отправляет P2P зашифрованное сообщение конкретному пользователю
        /// </summary>
        public async Task SendPrivateMessageAsync(string targetUserId, string content)
        {
            if (!_users.TryGetValue(targetUserId, out var targetUser))
            {
                SystemMessage?.Invoke("❌ Пользователь не найден");
                return;
            }

            if (!_peers.TryGetValue(targetUserId, out var connection))
            {
                SystemMessage?.Invoke("❌ Нет прямого соединения с пользователем");
                return;
            }

            try
            {
                var encrypted = _crypto.EncryptForPeer(targetUserId, content);

                var message = ChatMessage.CreateText(LocalUser.Id, LocalUser.Username, "[p2p-encrypted]");
                message.EncryptedContent = encrypted.EncryptedContent;
                message.EncryptedAesKey = encrypted.EncryptedAesKey;
                message.Iv = encrypted.Iv;
                message.TargetUserId = targetUserId;

                await connection.SendAsync(message);
                SystemMessage?.Invoke($"🔐 Приватное сообщение отправлено {targetUser.Username}");
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"❌ Ошибка P2P шифрования: {ex.Message}");
            }
        }

        /// <summary>
        /// Банит пользователя
        /// </summary>
        public async Task BanUserAsync(string userId)
        {
            if (LocalUser.Role != UserRole.Admin)
            {
                SystemMessage?.Invoke("❌ Только администратор может банить пользователей");
                return;
            }

            if (userId == LocalUser.Id)
            {
                SystemMessage?.Invoke("❌ Нельзя забанить себя");
                return;
            }

            if (!_users.TryGetValue(userId, out var user))
            {
                SystemMessage?.Invoke("❌ Пользователь не найден");
                return;
            }

            lock (_banLock)
            {
                _bannedUsers.Add(userId);
            }
            user.IsBanned = true;

            var banMessage = ChatMessage.CreateSystem(
                MessageType.Ban,
                LocalUser.Id,
                LocalUser.Username,
                userId);

            await BroadcastAsync(banMessage);

            // Отключаем забаненного пользователя
            if (_peers.TryGetValue(userId, out var connection))
            {
                connection.Dispose();
            }

            SystemMessage?.Invoke($"🚫 Пользователь {user.Username} забанен");
        }

        /// <summary>
        /// Разбанивает пользователя
        /// </summary>
        public async Task UnbanUserAsync(string userId)
        {
            if (LocalUser.Role != UserRole.Admin)
            {
                SystemMessage?.Invoke("❌ Только администратор может разбанивать пользователей");
                return;
            }

            bool wasBanned;
            lock (_banLock)
            {
                wasBanned = _bannedUsers.Remove(userId);
            }

            if (!wasBanned)
            {
                SystemMessage?.Invoke("❌ Пользователь не в бан-листе");
                return;
            }

            if (_users.TryGetValue(userId, out var user))
            {
                user.IsBanned = false;
            }

            var unbanMessage = ChatMessage.CreateSystem(
                MessageType.Unban,
                LocalUser.Id,
                LocalUser.Username,
                userId);

            await BroadcastAsync(unbanMessage);
            SystemMessage?.Invoke($"✅ Пользователь разбанен");
        }

        private void SetupConnectionHandlers(PeerConnection connection)
        {
            connection.MessageReceived += OnMessageReceived;
            connection.Disconnected += OnPeerDisconnected;
        }

        private async void OnMessageReceived(PeerConnection connection, ChatMessage message)
        {
            // Проверяем бан
            bool isBanned;
            lock (_banLock)
            {
                isBanned = _bannedUsers.Contains(message.SenderId);
            }

            if (isBanned)
            {
                return; // Игнорируем сообщения от забаненных
            }

            switch (message.Type)
            {
                case MessageType.Join:
                    await HandleJoinMessage(connection, message);
                    break;

                case MessageType.Leave:
                    HandleLeaveMessage(message);
                    break;

                case MessageType.Text:
                    HandleTextMessage(message);
                    break;

                case MessageType.KeyExchange:
                    HandleKeyExchange(message);
                    break;

                case MessageType.PeerList:
                    await HandlePeerList(message);
                    break;

                case MessageType.Ban:
                    HandleBanMessage(message);
                    break;

                case MessageType.Unban:
                    HandleUnbanMessage(message);
                    break;

                case MessageType.Ping:
                    await connection.SendAsync(ChatMessage.CreateSystem(
                        MessageType.Pong, LocalUser.Id, LocalUser.Username, "pong"));
                    break;
            }
        }

        private async Task HandleJoinMessage(PeerConnection connection, ChatMessage message)
        {
            try
            {
                var joinData = JsonSerializer.Deserialize<JoinData>(message.Content);
                if (joinData == null) return;

                // Проверяем, не зарегистрирован ли уже этот пользователь (защита от цикла)
                bool isNewUser = !_users.ContainsKey(message.SenderId);

                connection.SetPeerId(message.SenderId);
                _peers[message.SenderId] = connection;

                var newUser = new User
                {
                    Id = message.SenderId,
                    Username = message.SenderName,
                    PublicKey = joinData.PublicKey,
                    Endpoint = connection.PeerEndpoint,
                    Role = joinData.IsAdmin ? UserRole.Admin : UserRole.User
                };

                _users[message.SenderId] = newUser;
                _crypto.RegisterPeerPublicKey(message.SenderId, joinData.PublicKey);

                // Уведомляем только о новых пользователях
                if (isNewUser)
                {
                    UserJoined?.Invoke(newUser);

                    // Отправляем наш ответ только если это новый пользователь
                    await SendJoinMessageAsync(connection);

                    // Отправляем список известных пиров
                    await SendPeerListAsync(connection);
                }
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"Ошибка обработки Join: {ex.Message}");
            }
        }

        private void HandleLeaveMessage(ChatMessage message)
        {
            if (_users.TryRemove(message.SenderId, out var user))
            {
                _crypto.RemovePeerPublicKey(message.SenderId);
                UserLeft?.Invoke(user);
            }
        }

        private void HandleTextMessage(ChatMessage message)
        {
            try
            {
                string decryptedContent;

                if (!string.IsNullOrEmpty(message.TargetUserId))
                {
                    // P2P зашифрованное сообщение
                    if (message.TargetUserId != LocalUser.Id) return;

                    var payload = new EncryptedPayload
                    {
                        EncryptedContent = message.EncryptedContent!,
                        EncryptedAesKey = message.EncryptedAesKey!,
                        Iv = message.Iv!
                    };

                    decryptedContent = _crypto.DecryptFromPeer(payload);
                    SystemMessage?.Invoke($"🔐 Приватное от {message.SenderName}: {decryptedContent}");
                }
                else if (!string.IsNullOrEmpty(message.EncryptedContent))
                {
                    // Broadcast зашифрованное сообщение
                    var payload = new EncryptedPayload
                    {
                        EncryptedContent = message.EncryptedContent,
                        EncryptedAesKey = string.Empty,
                        Iv = message.Iv!
                    };

                    decryptedContent = _crypto.DecryptBroadcast(payload, _sharedChatKey);

                    var decryptedMessage = ChatMessage.CreateText(
                        message.SenderId,
                        message.SenderName,
                        decryptedContent);

                    MessageReceived?.Invoke(decryptedMessage);
                }
                else
                {
                    // Незашифрованное (для совместимости)
                    MessageReceived?.Invoke(message);
                }
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"⚠️ Ошибка расшифровки: {ex.Message}");
            }
        }

        private void HandleKeyExchange(ChatMessage message)
        {
            try
            {
                var keyData = JsonSerializer.Deserialize<KeyExchangeData>(message.Content);
                if (keyData != null)
                {
                    _sharedChatKey = Convert.FromBase64String(keyData.SharedKey);
                    SystemMessage?.Invoke("🔑 Получен общий ключ чата");
                }
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"Ошибка обмена ключами: {ex.Message}");
            }
        }

        private async Task HandlePeerList(ChatMessage message)
        {
            try
            {
                var peerList = JsonSerializer.Deserialize<List<PeerInfo>>(message.Content);
                if (peerList == null) return;

                foreach (var peer in peerList)
                {
                    // Не подключаемся к себе
                    if (peer.Id == LocalUser.Id) continue;

                    // Не подключаемся, если уже есть соединение
                    if (_peers.ContainsKey(peer.Id)) continue;

                    // Пытаемся подключиться
                    var parts = peer.Endpoint.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                    {
                        await ConnectToPeerAsync(parts[0], port);
                    }
                }
            }
            catch (Exception ex)
            {
                SystemMessage?.Invoke($"Ошибка обработки списка пиров: {ex.Message}");
            }
        }

        private void HandleBanMessage(ChatMessage message)
        {
            var bannedUserId = message.Content;

            // Только админ может банить
            if (_users.TryGetValue(message.SenderId, out var sender) && sender.Role == UserRole.Admin)
            {
                lock (_banLock)
                {
                    _bannedUsers.Add(bannedUserId);
                }

                if (_users.TryGetValue(bannedUserId, out var bannedUser))
                {
                    bannedUser.IsBanned = true;
                    SystemMessage?.Invoke($"🚫 {bannedUser.Username} был забанен администратором {sender.Username}");
                }

                // Если забанили нас - отключаемся
                if (bannedUserId == LocalUser.Id)
                {
                    SystemMessage?.Invoke("🚫 Вы были забанены!");
                    Dispose();
                }
            }
        }

        private void HandleUnbanMessage(ChatMessage message)
        {
            var unbannedUserId = message.Content;

            if (_users.TryGetValue(message.SenderId, out var sender) && sender.Role == UserRole.Admin)
            {
                lock (_banLock)
                {
                    _bannedUsers.Remove(unbannedUserId);
                }

                if (_users.TryGetValue(unbannedUserId, out var unbannedUser))
                {
                    unbannedUser.IsBanned = false;
                    SystemMessage?.Invoke($"✅ {unbannedUser.Username} был разбанен администратором {sender.Username}");
                }
            }
        }

        private void OnPeerDisconnected(PeerConnection connection)
        {
            if (!string.IsNullOrEmpty(connection.PeerId))
            {
                _peers.TryRemove(connection.PeerId, out _);

                if (_users.TryRemove(connection.PeerId, out var user))
                {
                    _crypto.RemovePeerPublicKey(connection.PeerId);
                    UserLeft?.Invoke(user);
                }
            }

            connection.Dispose();
        }

        private async Task SendJoinMessageAsync(PeerConnection connection)
        {
            var joinData = new JoinData
            {
                PublicKey = _crypto.PublicKey,
                IsAdmin = LocalUser.Role == UserRole.Admin
            };

            var message = ChatMessage.CreateSystem(
                MessageType.Join,
                LocalUser.Id,
                LocalUser.Username,
                JsonSerializer.Serialize(joinData));

            await connection.SendAsync(message);
        }

        private async Task SendPeerListAsync(PeerConnection connection)
        {
            var peerList = _users.Values
                .Where(u => u.Id != connection.PeerId)
                .Select(u => new PeerInfo { Id = u.Id, Endpoint = u.Endpoint })
                .ToList();

            if (peerList.Count == 0) return;

            var message = ChatMessage.CreateSystem(
                MessageType.PeerList,
                LocalUser.Id,
                LocalUser.Username,
                JsonSerializer.Serialize(peerList));

            await connection.SendAsync(message);

            // Также отправляем общий ключ чата
            if (LocalUser.Role == UserRole.Admin)
            {
                var keyData = new KeyExchangeData { SharedKey = Convert.ToBase64String(_sharedChatKey) };
                var keyMessage = ChatMessage.CreateSystem(
                    MessageType.KeyExchange,
                    LocalUser.Id,
                    LocalUser.Username,
                    JsonSerializer.Serialize(keyData));

                await connection.SendAsync(keyMessage);
            }
        }

        private async Task BroadcastAsync(ChatMessage message)
        {
            var tasks = _peers.Values.Select(p => p.SendAsync(message));
            await Task.WhenAll(tasks);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();

            foreach (var peer in _peers.Values)
            {
                peer.Dispose();
            }

            _crypto.Dispose();
            _cts.Dispose();
        }
    }

    internal sealed class JoinData
    {
        public required string PublicKey { get; init; }
        public bool IsAdmin { get; init; }
    }

    internal sealed class PeerInfo
    {
        public required string Id { get; init; }
        public required string Endpoint { get; init; }
    }

    internal sealed class KeyExchangeData
    {
        public required string SharedKey { get; init; }
    }
}
