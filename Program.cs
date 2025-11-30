using ConsoleChat.Network;
using ConsoleChat.UI;

namespace ConsoleChat
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "ООО \"Инфозонт\"";

            try
            {
                var (username, port, isAdmin) = ParseArguments(args);

                if (string.IsNullOrEmpty(username))
                {
                    (username, port, isAdmin) = await InteractiveSetup();
                }

                Console.WriteLine($"\n🚀 Запуск узла {username} на порту {port}...");

                using var node = new P2PNode(username, port, isAdmin);
                using var ui = new ConsoleUI(node);

                // Запускаем сервер в фоне
                var serverTask = node.StartAsync();

                // Если указан начальный пир, подключаемся
                var connectTo = GetConnectArgument(args);
                if (!string.IsNullOrEmpty(connectTo))
                {
                    var parts = connectTo.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[1], out var peerPort))
                    {
                        await Task.Delay(500); // Даём серверу запуститься
                        await node.ConnectToPeerAsync(parts[0], peerPort);
                    }
                }

                // Запускаем UI
                await ui.RunAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Критическая ошибка: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        private static (string Username, int Port, bool IsAdmin) ParseArguments(string[] args)
        {
            string? username = null;
            int port = 0;
            bool isAdmin = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-u":
                    case "--username":
                        if (i + 1 < args.Length) username = args[++i];
                        break;

                    case "-p":
                    case "--port":
                        if (i + 1 < args.Length) int.TryParse(args[++i], out port);
                        break;

                    case "-a":
                    case "--admin":
                        isAdmin = true;
                        break;

                    case "-h":
                    case "--help":
                        PrintUsage();
                        Environment.Exit(0);
                        break;
                }
            }

            return (username ?? string.Empty, port, isAdmin);
        }

        private static string? GetConnectArgument(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-c" || args[i] == "--connect") && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static async Task<(string Username, int Port, bool IsAdmin)> InteractiveSetup()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
  ╔═══════════════════════════════════════════════════════════════╗
  ║                 🔐 ConsoleChat - Децентрализованный чат       ║
  ╚═══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            // Username
            Console.Write("\n  👤 Введите ваше имя: ");
            var username = Console.ReadLine()?.Trim();
            while (string.IsNullOrWhiteSpace(username))
            {
                Console.Write("  ⚠️  Имя не может быть пустым. Введите имя: ");
                username = Console.ReadLine()?.Trim();
            }
            Console.Title = $"ООО \"Инфозонт\" - {username}";

            // Port
            int port = 0;
            while (port < 1024 || port > 65535)
            {
                Console.Write("  🔌 Введите порт (1024-65535): ");
                var portInput = Console.ReadLine();

                if (!int.TryParse(portInput, out port) || port < 1024 || port > 65535)
                {
                    Console.WriteLine("  ⚠️  Некорректный порт. Введите число от 1024 до 65535.");
                    port = 0;
                }
            }

            // Admin
            Console.Write("  👑 Запустить как администратор? (y/N): ");
            var adminInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            var isAdmin = adminInput == "y" || adminInput == "yes" || adminInput == "да";

            return (username, port, isAdmin);
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"
ConsoleChat - Децентрализованный чат с шифрованием

Использование:
  ConsoleChat [options]

Параметры:
  -u, --username <name>    Имя пользователя
  -p, --port <port>        Порт для прослушивания (1024-65535)
  -a, --admin              Запустить с правами администратора
  -c, --connect <host:port> Подключиться к указанному узлу при старте
  -h, --help               Показать эту справку

Примеры:
  ConsoleChat -u Timur -p 5000 -a                  # Запуск админа на порту 5000
  ConsoleChat -u Ayrat -p 5001 -c 127.0.0.1:5000     # Подключение к Timur

Возможности:
  ✅ Децентрализованная P2P архитектура
  ✅ Роли пользователей (Admin/User)
  ✅ Бан пользователей администратором
  ✅ Текстовые смайлики (:) :D и др.)
  ✅ AES-256 шифрование broadcast-сообщений
  ✅ RSA-2048 + AES P2P шифрование приватных сообщений
");
        }
    }
}
