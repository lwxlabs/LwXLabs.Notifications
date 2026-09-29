using System.Text.Json;
using LwXLabs.Notifications;
using Xunit;

namespace LwXLabs.Notifications.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Component_payload_is_real_json()
    {
        var component = NotificationComponent.Progress("Backup", 0.72, "Uploading");
        var json = JsonSerializer.Serialize(component, NotificationJson.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(0.72, document.RootElement.GetProperty("data").GetProperty("value").GetDouble(), 3);
    }

    [Fact]
    public void Transport_topic_is_stable_and_app_scoped()
    {
        var a = NotificationTopics.ToTransportTopic("app-a", "alerts");
        var b = NotificationTopics.ToTransportTopic("app-a", "alerts");
        var c = NotificationTopics.ToTransportTopic("app-b", "alerts");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("lwx_", a);
    }
}
