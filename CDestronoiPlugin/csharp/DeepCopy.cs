using System.Text.Json;

public static class DeepCloneExtensions
{
    public static T DeepClone<T>(this T obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<T>(json);
    }
}
