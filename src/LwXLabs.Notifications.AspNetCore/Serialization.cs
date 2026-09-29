using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LwXLabs.Notifications.AspNetCore;

internal static class NotificationSerialization
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, NotificationJson.SerializerOptions);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, NotificationJson.SerializerOptions)
        ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");

    public static List<string> Topics(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, NotificationJson.SerializerOptions) ?? new List<string>();
}
