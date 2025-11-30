using ConsoleChat.Models;
using ConsoleChat.Network;

namespace ConsoleChat.UI
{
    /// <summary>
    /// Консольный интерфейс чата
    /// </summary>
    public sealed class ConsoleUI : IDisposable
    {
        private readonly P2PNode _node;
        private readonly CancellationTokenSource _cts = new();
        private bool _isRunning = true;

        public ConsoleUI(P2PNode node)
        {
            _node = node;

            // Подписываемся на события
            _node.MessageReceived += OnMessageReceived;
            _node.UserJoined += OnUserJoined;
            _node.UserLeft += OnUserLeft;
            _node.SystemMessage += OnSystemMessage;
        }

        /// <summary>
        /// Запускает главный цикл UI
        /// </summary>
        public async Task RunAsync()
        {
            Console.Clear();
            PrintHeader();
            PrintHelp();

            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                Console.Write($"\n[{_node.LocalUser.Username}] > ");

                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                await ProcessInputAsync(input);
            }
        }

        private async Task ProcessInputAsync(string input)
        {
            if (input.StartsWith('/'))
            {
                await ProcessCommandAsync(input);
            }
            else
            {
                // Обрабатываем смайлики и отправляем сообщение
                var processedMessage = EmojiService.ProcessEmojis(input);
                await _node.BroadcastMessageAsync(processedMessage);
            }
        }

        private async Task ProcessCommandAsync(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLowerInvariant();

            switch (command)
            {
                case "/help":
                case "/h":
                case "/?":
                    PrintHelp();
                    break;

                case "/connect":
                case "/c":
                    await HandleConnectCommand(parts);
                    break;

                case "/users":
                case "/u":
                    PrintUsers();
                    break;

                case "/pm":
                case "/private":
                    await HandlePrivateMessage(parts);
                    break;

                case "/ban":
                    await HandleBanCommand(parts);
                    break;

                case "/unban":
                    await HandleUnbanCommand(parts);
                    break;

                case "/banlist":
                    PrintBanList();
                    break;

                case "/emojis":
                    HandleEmojisCommand(parts);
                    break;

                case "/me":
                    PrintMyInfo();
                    break;

                case "/clear":
                case "/cls":
                    Console.Clear();
                    PrintHeader();
                    break;

                case "/quit":
                case "/exit":
                case "/q":
                    _isRunning = false;
                    Console.WriteLine("\n👋 Выход из чата...");
                    break;

                default:
                    Console.WriteLine($"❓ Неизвестная команда: {command}. Введите /help для справки.");
                    break;
            }
        }

        private async Task HandleConnectCommand(string[] parts)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Использование: /connect <host:port> или /connect <host> <port>");
                return;
            }

            string host;
            int port;

            if (parts.Length == 2 && parts[1].Contains(':'))
            {
                var hostParts = parts[1].Split(':');
                host = hostParts[0];
                if (!int.TryParse(hostParts[1], out port))
                {
                    Console.WriteLine("❌ Некорректный порт");
                    return;
                }
            }
            else if (parts.Length >= 3)
            {
                host = parts[1];
                if (!int.TryParse(parts[2], out port))
                {
                    Console.WriteLine("❌ Некорректный порт");
                    return;
                }
            }
            else
            {
                Console.WriteLine("Использование: /connect <host:port> или /connect <host> <port>");
                return;
            }

            Console.WriteLine($"🔄 Подключение к {host}:{port}...");
            await _node.ConnectToPeerAsync(host, port);
        }

        private async Task HandlePrivateMessage(string[] parts)
        {
            if (parts.Length < 3)
            {
                Console.WriteLine("Использование: /pm <username> <сообщение>");
                Console.WriteLine("Пример: /pm Timur Привет! Это приватное сообщение.");
                return;
            }

            var targetUsername = parts[1];
            var message = string.Join(' ', parts.Skip(2));

            var targetUser = _node.Users.Values
                .FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

            if (targetUser == null)
            {
                Console.WriteLine($"❌ Пользователь '{targetUsername}' не найден");
                return;
            }

            if (targetUser.Id == _node.LocalUser.Id)
            {
                Console.WriteLine("❌ Нельзя отправить приватное сообщение себе");
                return;
            }

            var processedMessage = EmojiService.ProcessEmojis(message);
            await _node.SendPrivateMessageAsync(targetUser.Id, processedMessage);
        }

        private async Task HandleBanCommand(string[] parts)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Использование: /ban <username>");
                return;
            }

            var targetUsername = parts[1];
            var targetUser = _node.Users.Values
                .FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

            if (targetUser == null)
            {
                Console.WriteLine($"❌ Пользователь '{targetUsername}' не найден");
                return;
            }

            await _node.BanUserAsync(targetUser.Id);
        }

        private async Task HandleUnbanCommand(string[] parts)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Использование: /unban <username>");
                return;
            }

            var targetUsername = parts[1];
            var targetUser = _node.Users.Values
                .FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));

            if (targetUser == null)
            {
                // Попробуем найти в бан-листе по ID (если пользователь уже отключился)
                Console.WriteLine($"❌ Пользователь '{targetUsername}' не найден в онлайн-списке");
                return;
            }

            await _node.UnbanUserAsync(targetUser.Id);
        }

        private void PrintBanList()
        {
            var bannedIds = _node.BannedUserIds;

            if (bannedIds.Count == 0)
            {
                Console.WriteLine("📋 Бан-лист пуст");
                return;
            }

            Console.WriteLine("\n🚫 Забаненные пользователи:");
            foreach (var id in bannedIds)
            {
                var user = _node.Users.Values.FirstOrDefault(u => u.Id == id);
                var name = user?.Username ?? $"[ID: {id[..8]}...]";
                Console.WriteLine($"  • {name}");
            }
        }

        private void HandleEmojisCommand(string[] parts)
        {
            EmojiService.PrintAllEmojis();
        }

        private void PrintUsers()
        {
            Console.WriteLine("\n👥 Пользователи онлайн:");

            foreach (var user in _node.Users.Values.OrderByDescending(u => u.Role))
            {
                var roleIcon = user.Role == UserRole.Admin ? "👑" : "👤";
                var statusIcon = user.IsBanned ? "🚫" : "🟢";
                var isMe = user.Id == _node.LocalUser.Id ? " (вы)" : "";

                Console.WriteLine($"  {statusIcon} {roleIcon} {user.Username}{isMe}");
            }

            Console.WriteLine($"\nВсего: {_node.Users.Count} пользователей");
        }

        private void PrintMyInfo()
        {
            var user = _node.LocalUser;
            Console.WriteLine("\n📄 Ваш профиль:");
            Console.WriteLine($"  • ID: {user.Id[..8]}...");
            Console.WriteLine($"  • Имя: {user.Username}");
            Console.WriteLine($"  • Роль: {(user.Role == UserRole.Admin ? "👑 Администратор" : "👤 Пользователь")}");
            Console.WriteLine($"  • Порт: {_node.Port}");
            Console.WriteLine($"  • Публичный ключ: {user.PublicKey[..32]}...");
        }

        private void PrintHeader()
        {
            var role = _node.LocalUser.Role == UserRole.Admin ? "👑 ADMIN" : "👤 USER";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
  ╔═══════════════════════════════════════════════════════════════╗
  ║                     🔐 ConsoleChat                            ║
  ║          Децентрализованный чат с шифрованием                 ║
  ╚═══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.WriteLine($"\n  📡 Порт: {_node.Port} | {role}\n");
        }

        private void PrintHelp()
        {
            Console.WriteLine(@"
  📋 Команды:
  ────────────────────────────────────────────────────────
  /connect <host:port>  - Подключиться к другому узлу
  /users                - Список пользователей онлайн
  /pm <user> <msg>      - Приватное P2P-зашифрованное сообщение
  /emojis               - Показать смайлики
  /me                   - Показать информацию о себе
  /clear                - Очистить экран");

            if (_node.LocalUser.Role == UserRole.Admin)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(@"  
  👑 Команды администратора:
  /ban <user>           - Забанить пользователя
  /unban <user>         - Разбанить пользователя
  /banlist              - Показать бан-лист");
                Console.ResetColor();
            }

            Console.WriteLine(@"
  /quit                 - Выйти из чата
  ────────────────────────────────────────────────────────
  🔐 Все сообщения шифруются AES-256 + RSA-2048.");
        }

        private void OnMessageReceived(ChatMessage message)
        {
            var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var isEncrypted = !string.IsNullOrEmpty(message.EncryptedContent) || !string.IsNullOrEmpty(message.EncryptedAesKey);
            var lockIcon = isEncrypted ? "🔐 " : "";
            var isOwnMessage = message.SenderId == _node.LocalUser.Id;

            // Очищаем текущий ввод и показываем сообщение
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

            if (isOwnMessage)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{timestamp}] {lockIcon}Вы: {message.Content}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"[{timestamp}] {lockIcon}{message.SenderName}: {message.Content}");
            }

            Console.ResetColor();

            if (!isOwnMessage)
            {
                Console.Write($"[{_node.LocalUser.Username}] > ");
            }
        }

        private void OnUserJoined(User user)
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ {user.Username} присоединился к чату");
            Console.ResetColor();
            Console.Write($"[{_node.LocalUser.Username}] > ");
        }

        private void OnUserLeft(User user)
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"👋 {user.Username} покинул чат");
            Console.ResetColor();
            Console.Write($"[{_node.LocalUser.Username}] > ");
        }

        private void OnSystemMessage(string message)
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"ℹ️  {message}");
            Console.ResetColor();
            Console.Write($"[{_node.LocalUser.Username}] > ");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _node.MessageReceived -= OnMessageReceived;
            _node.UserJoined -= OnUserJoined;
            _node.UserLeft -= OnUserLeft;
            _node.SystemMessage -= OnSystemMessage;
            _cts.Dispose();
        }
    }
}
