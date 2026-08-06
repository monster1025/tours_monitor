using System.IO.Compression;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace TourMonitor.LevelTravel;

/// <summary>
/// Расшифровка ответов API Level.Travel.
/// Формат тела (если начинается с "!/"): base64url(
///     байт(23 XOR индекс ключа), nonce(24), NaCl secretbox(тег(16) + шифротекст) )
/// затем zlib-inflate и JSON.
/// Secretbox = XSalsa20-Poly1305: ключ Poly1305 = первые 32 байта keystream, тег MAC — в начале.
/// </summary>
public static class LevelTravelCrypto
{
    private const string Prefix = "!/";

    /// <summary>Декодирует ответ в строку JSON; если тело не зашифровано — возвращает как есть.</summary>
    public static string DecryptToJson(string body, IReadOnlyList<string> secretBoxKeys)
    {
        if (!body.StartsWith(Prefix, StringComparison.Ordinal))
            return body;

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(body.Substring(2).Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException)
        {
            throw new LevelTravelException("Не удалось декодировать base64 в теле ответа.");
        }

        if (payload.Length < 25)
            throw new LevelTravelException("Тело ответа слишком короткое для расшифровки.");

        int keyIndex = (payload[0] ^ 23) - '0';
        if (keyIndex < 1 || keyIndex > secretBoxKeys.Count)
            throw new LevelTravelException($"Неизвестный индекс ключа расшифровки: {keyIndex}.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secretBoxKeys[keyIndex - 1]);
        }
        catch (FormatException)
        {
            throw new LevelTravelException("Неверный формат ключа расшифровки в конфигурации.");
        }

        var nonce = payload.AsSpan(1, 24).ToArray();
        var ciphertext = payload.AsSpan(25).ToArray();

        var plaintext = SecretBoxOpen(ciphertext, nonce, key)
            ?? throw new LevelTravelException("Не удалось расшифровать ответ (неверный MAC).");

        using var zlib = new ZLibStream(new MemoryStream(plaintext), CompressionMode.Decompress);
        using var reader = new StreamReader(zlib, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[]? SecretBoxOpen(byte[] ciphertext, byte[] nonce, byte[] key)
    {
        var engine = new XSalsa20Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));

        var streamLength = 32 + ciphertext.Length;
        var stream = new byte[streamLength];
        engine.ProcessBytes(new byte[streamLength], 0, streamLength, stream, 0);

        var polyKey = stream.AsSpan(0, 32).ToArray();
        var data = ciphertext.AsSpan(16).ToArray();
        var receivedTag = ciphertext.AsSpan(0, 16).ToArray();

        var mac = new Poly1305();
        mac.Init(new KeyParameter(polyKey));
        mac.BlockUpdate(data, 0, data.Length);
        var tag = new byte[16];
        mac.DoFinal(tag, 0);

        if (!FixedTimeEquals(tag, receivedTag))
            return null;

        for (var i = 0; i < data.Length; i++)
            data[i] ^= stream[32 + i];
        return data;
    }

    private static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

public sealed class LevelTravelException : Exception
{
    public LevelTravelException(string message) : base(message) { }
    public LevelTravelException(string message, Exception inner) : base(message, inner) { }
}
