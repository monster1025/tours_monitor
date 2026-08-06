using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TourMonitor.LevelTravel;

/// <summary>
/// Воспроизводит алгоритм подписи запросов веб-клиента Level.Travel:
/// sign = md5( sorted(значения параметров, JSON без кавычек).join("") + последний_сегмент_пути + key + salt )
/// Значения рекурсивно разворачиваются (массивы/объекты — по значениям), пустые массивы/объекты пропускаются.
/// </summary>
public static class SignHelper
{
    public static string ComputeGet(string path, IDictionary<string, object?> parameters, string key, string salt, string apiVersion)
    {
        var withMeta = new Dictionary<string, object?>(parameters)
        {
            ["key"] = key,
            ["api_version"] = apiVersion,
            ["js"] = "true",
        };
        return Compute(path, withMeta.Values, key, salt);
    }

    public static string Compute(string path, IEnumerable<object?> values, string key, string salt)
    {
        var flattened = new List<object?>();
        Flatten(values, flattened);

        var mapped = new List<string>();
        foreach (var value in flattened)
        {
            var json = JsonSerializer.Serialize(value);
            if (json is "\"[]\"" or "\"{}\"" && value is not string)
                json = "";
            mapped.Add(json.Replace("\"", ""));
        }

        mapped.Sort(StringComparer.Ordinal);

        var lastSegment = path.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        var source = string.Concat(mapped) + lastSegment + key + salt;
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static void Flatten(IEnumerable<object?> values, List<object?> output)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case string s:
                    output.Add(s.Trim());
                    break;
                case System.Collections.IDictionary dict:
                    // как Object.values(obj) в JS: берём только значения
                    var dictItems = dict.Values.Cast<object?>().ToList();
                    if (dictItems.Count > 0)
                        Flatten(dictItems, output);
                    break;
                case System.Collections.IEnumerable seq:
                    // как Object.values(array) в JS: пустой массив значений не даёт
                    var items = seq.Cast<object?>().ToList();
                    if (items.Count > 0)
                        Flatten(items, output);
                    break;
                default:
                    output.Add(value);
                    break;
            }
        }
    }
}
