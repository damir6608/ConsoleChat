using System.Text.RegularExpressions;

namespace ConsoleChat.UI
{
    /// <summary>
    /// Сервис для обработки текстовых смайликов
    /// </summary>
    public static class EmojiService
    {
        private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Эмоции
            { ":)", "😊" },
            { ":-)", "😊" },
            { ":D", "😄" },
            { ":-D", "😄" },
            { ":P", "😛" },
            { ":-P", "😛" },
            { ";)", "😉" },
            { ";-)", "😉" },
            { ":angry:", "😠" },
            { ":think:", "🤔" },
            { ":lol:", "🤣" },
        
            // Жесты
            { ":+1:", "👍" },
            { ":-1:", "👎" },
            { ":ok:", "👌" },
        
            // Объекты
            { ":pizza:", "🍕" },
            { ":cake:", "🎂" },
            { ":gift:", "🎁" },
        
            // Чат-специфичные
            { ":ban:", "🚫" },
            { ":admin:", "👑" },
            { ":user:", "👤" },
            { ":online:", "🟢" },
            { ":offline:", "🔴" },
            { ":encrypted:", "🔐" },
            { ":private:", "🔒" }
        };
        
        private static readonly Regex EmojiRegex = BuildEmojiRegex();

        /// <summary>
        /// Строит Regex из всех кодов эмодзи.
        /// </summary>
        private static Regex BuildEmojiRegex()
        {
            var patterns = EmojiMap.Keys
                .OrderByDescending(k => k.Length)
                .Select(Regex.Escape);

            var pattern = string.Join("|", patterns);

            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        
        /// <summary>
        /// Преобразует текст в эмодзи.
        /// </summary>
        public static string ProcessEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            if (!MightContainEmoji(text)) return text;

            return EmojiRegex.Replace(text, match =>
                EmojiMap.TryGetValue(match.Value, out var emoji) ? emoji : match.Value);
        }

        /// <summary>
        /// Проверка на возможное присутствие эмодзи.
        /// </summary>
        private static bool MightContainEmoji(string text)
        {
            foreach (var c in text)
            {
                if (c == ':' || c == ';' || c == '(')
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Возвращает список доступных смайликов
        /// </summary>
        public static IEnumerable<(string Code, string Emoji)> GetAvailableEmojis()
        {
            return EmojiMap.Select(kvp => (kvp.Key, kvp.Value));
        }

        /// <summary>
        /// Выводит полный список смайликов
        /// </summary>
        public static void PrintAllEmojis()
        {
            Console.WriteLine("\n📋 Все доступные смайлики:\n");

            var items = EmojiMap.Select(kvp => $"{kvp.Key} → {kvp.Value}").ToList();

            for (int i = 0; i < items.Count; i += 4)
            {
                var line = string.Join("  ", items.Skip(i).Take(4).Select(s => s.PadRight(18)));
                Console.WriteLine($"  {line}");
            }

            Console.WriteLine();
        }
    }
}
