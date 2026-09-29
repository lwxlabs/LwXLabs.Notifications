using System;
using System.Collections.Generic;

namespace LwXLabs.Notifications.Firebase;

public sealed class FirebaseNotificationsOptions
{
    public int InlinePayloadByteLimit { get; set; } = 1800;
    /// <summary>Default Firebase project shared by notification applications.</summary>
    public string ProjectId { get; set; } = string.Empty;
    public Dictionary<string, FirebaseApplicationOptions> Applications { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FirebaseApplicationOptions
{
    public string ProjectId { get; set; } = string.Empty;
}
