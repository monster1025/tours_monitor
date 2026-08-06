using System.Text.Json;
using TourMonitor.LevelTravel;

namespace TourMonitor.Tests;

public class CryptoTests
{
    /// <summary>
    /// Реальный зашифрованный ответ API (captured: /tmp/lt_probe/cap_search/069-resp.json).
    /// Проверяет secretbox-дешифровку (XSalsa20-Poly1305) и zlib-inflate.
    /// </summary>
    [Fact]
    public void DecryptToJson_Decrypts_CapturedEnqueueResponse()
    {
        var fixture = File.ReadAllText(Path.Combine("Fixtures", "enqueue_encrypted.txt"));

        var json = LevelTravelCrypto.DecryptToJson(fixture, TestData.SecretBoxKeys);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var requestId = doc.RootElement.GetProperty("request_id").GetString();
        Assert.Equal("MjEzfDIyNXwxMTgxMjN8MTA1Nnx8MjAyNi0wOC0yMCwyMDI2LTA4LTIwfDB8OSw5fDJ8MHx8fDB8MHx8fDY=", requestId);
    }

    /// <summary>Незашифрованные тела (v3.7 без секрета) возвращаются как есть.</summary>
    [Fact]
    public void DecryptToJson_PassesThrough_PlainBody()
    {
        const string plain = "{\"success\":true}";

        var result = LevelTravelCrypto.DecryptToJson(plain, TestData.SecretBoxKeys);

        Assert.Equal(plain, result);
    }

    /// <summary>Мусор после префикса "!/" должен давать осмысленную ошибку, а не исключение-дамп.</summary>
    [Fact]
    public void DecryptToJson_Throws_LevelTravelException_OnGarbage()
    {
        Assert.Throws<LevelTravelException>(() => LevelTravelCrypto.DecryptToJson("!/not-base64!!", TestData.SecretBoxKeys));
    }

    /// <summary>Неизвестный индекс ключа (не 1..7) отклоняется.</summary>
    [Fact]
    public void DecryptToJson_Throws_OnUnknownKeyIndex()
    {
        // первый байт выбирает индекс: (payload[0] ^ 23) - '0' должен быть вне 1..7
        var body = "!/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        var ex = Assert.Throws<LevelTravelException>(() => LevelTravelCrypto.DecryptToJson(body, TestData.SecretBoxKeys));
        Assert.Contains("индекс ключа", ex.Message);
    }
}
