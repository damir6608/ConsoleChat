using ConsoleChat.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleChat.Network
{
    /// <summary>
    /// Управляет соединением с одним пиром
    /// </summary>
    public sealed class PeerConnection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly CancellationTokenSource _cts = new();

        public string PeerId { get; private set; } = string.Empty;
        public string PeerEndpoint { get; }
        public bool IsConnected => _client.Connected;

        public event Action<PeerConnection, ChatMessage>? MessageReceived;
        public event Action<PeerConnection>? Disconnected;

        public PeerConnection(TcpClient client, string endpoint)
        {
            _client = client;
            _stream = client.GetStream();
            PeerEndpoint = endpoint;
        }

        public void SetPeerId(string peerId) => PeerId = peerId;

        /// <summary>
        /// Начинает асинхронное получение сообщений
        /// </summary>
        public void StartReceiving()
        {
            Task.Run(async () =>
            {
                var buffer = new byte[8192];
                var messageBuffer = new StringBuilder();

                try
                {
                    while (!_cts.Token.IsCancellationRequested && _client.Connected)
                    {
                        var bytesRead = await _stream.ReadAsync(buffer, _cts.Token);
                        if (bytesRead == 0) break;

                        messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                        // Обрабатываем все полные сообщения (разделены \n)
                        var content = messageBuffer.ToString();
                        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                        // Если последний символ не \n, последняя строка неполная
                        var lastComplete = content.EndsWith('\n') ? lines.Length : lines.Length - 1;

                        for (int i = 0; i < lastComplete; i++)
                        {
                            try
                            {
                                var message = JsonSerializer.Deserialize<ChatMessage>(lines[i]);
                                if (message != null)
                                {
                                    MessageReceived?.Invoke(this, message);
                                }
                            }
                            catch (JsonException)
                            {
                                // Игнорируем некорректные сообщения
                            }
                        }

                        // Оставляем неполное сообщение в буфере
                        messageBuffer.Clear();
                        if (lastComplete < lines.Length)
                        {
                            messageBuffer.Append(lines[^1]);
                        }
                    }
                }
                catch (Exception) when (_cts.Token.IsCancellationRequested)
                {
                    // Ожидаемое завершение
                }
                catch (Exception)
                {
                    // Соединение разорвано
                }
                finally
                {
                    Disconnected?.Invoke(this);
                }
            }, _cts.Token);
        }

        /// <summary>
        /// Отправляет сообщение пиру
        /// </summary>
        public async Task SendAsync(ChatMessage message)
        {
            if (!IsConnected) return;

            try
            {
                var json = JsonSerializer.Serialize(message) + "\n";
                var bytes = Encoding.UTF8.GetBytes(json);
                await _stream.WriteAsync(bytes, _cts.Token);
            }
            catch (Exception)
            {
                Disconnected?.Invoke(this);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _stream.Dispose();
            _client.Dispose();
            _cts.Dispose();
        }
    }
}
