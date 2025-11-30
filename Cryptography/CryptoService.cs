using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleChat.Cryptography
{
    /// <summary>
    /// Сервис криптографии для P2P шифрования
    /// Использует RSA и AES для шифрования сообщений
    /// </summary>
    public sealed class CryptoService : IDisposable
    {
        private readonly RSA _rsa;
        private readonly Dictionary<string, string> _peerPublicKeys = new();

        public string PublicKey { get; }

        public CryptoService()
        {
            _rsa = RSA.Create(2048);
            PublicKey = Convert.ToBase64String(_rsa.ExportRSAPublicKey());
        }

        /// <summary>
        /// Регистрирует публичный ключ пира для P2P шифрования
        /// </summary>
        public void RegisterPeerPublicKey(string peerId, string publicKey)
        {
            _peerPublicKeys[peerId] = publicKey;
        }

        /// <summary>
        /// Удаляет публичный ключ пира
        /// </summary>
        public void RemovePeerPublicKey(string peerId)
        {
            _peerPublicKeys.Remove(peerId);
        }

        /// <summary>
        /// Шифрует сообщение для конкретного пира (P2P encryption)
        /// </summary>
        public EncryptedPayload EncryptForPeer(string peerId, string plainText)
        {
            if (!_peerPublicKeys.TryGetValue(peerId, out var peerPublicKey))
                throw new InvalidOperationException($"Public key for peer {peerId} not found");

            // Генерируем случайный AES ключ для этого сообщения
            using var aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();

            // Шифруем сообщение AES
            var encryptedContent = EncryptAes(plainText, aes.Key, aes.IV);

            // Шифруем AES ключ публичным ключом получателя (RSA)
            using var peerRsa = RSA.Create();
            peerRsa.ImportRSAPublicKey(Convert.FromBase64String(peerPublicKey), out _);
            var encryptedAesKey = peerRsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);

            return new EncryptedPayload
            {
                EncryptedContent = Convert.ToBase64String(encryptedContent),
                EncryptedAesKey = Convert.ToBase64String(encryptedAesKey),
                Iv = Convert.ToBase64String(aes.IV)
            };
        }

        /// <summary>
        /// Расшифровывает сообщение от пира
        /// </summary>
        public string DecryptFromPeer(EncryptedPayload payload)
        {
            // Расшифровываем AES ключ нашим приватным ключом
            var encryptedAesKey = Convert.FromBase64String(payload.EncryptedAesKey);
            var aesKey = _rsa.Decrypt(encryptedAesKey, RSAEncryptionPadding.OaepSHA256);

            // Расшифровываем сообщение AES
            var encryptedContent = Convert.FromBase64String(payload.EncryptedContent);
            var iv = Convert.FromBase64String(payload.Iv);

            return DecryptAes(encryptedContent, aesKey, iv);
        }

        /// <summary>
        /// Шифрует сообщение для broadcast (общий ключ чата)
        /// </summary>
        public EncryptedPayload EncryptBroadcast(string plainText, byte[] sharedKey)
        {
            using var aes = Aes.Create();
            aes.Key = sharedKey;
            aes.GenerateIV();

            var encryptedContent = EncryptAes(plainText, aes.Key, aes.IV);

            return new EncryptedPayload
            {
                EncryptedContent = Convert.ToBase64String(encryptedContent),
                EncryptedAesKey = string.Empty, // Общий ключ известен всем
                Iv = Convert.ToBase64String(aes.IV)
            };
        }

        /// <summary>
        /// Расшифровывает broadcast сообщение
        /// </summary>
        public string DecryptBroadcast(EncryptedPayload payload, byte[] sharedKey)
        {
            var encryptedContent = Convert.FromBase64String(payload.EncryptedContent);
            var iv = Convert.FromBase64String(payload.Iv);

            return DecryptAes(encryptedContent, sharedKey, iv);
        }

        private static byte[] EncryptAes(string plainText, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        }

        private static string DecryptAes(byte[] cipherText, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return Encoding.UTF8.GetString(decryptedBytes);
        }

        public void Dispose() => _rsa.Dispose();
    }

    /// <summary>
    /// Зашифрованная полезная нагрузка
    /// </summary>
    public sealed class EncryptedPayload
    {
        public required string EncryptedContent { get; init; }
        public required string EncryptedAesKey { get; init; }
        public required string Iv { get; init; }
    }
}
