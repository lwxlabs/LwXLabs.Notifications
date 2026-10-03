using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Android.Content;

namespace LwXLabs.Notifications.Firebase.Android;

internal sealed class AndroidFirebaseJournalStorage : IFirebaseAndroidJournalStorage
{
    private const string PreferencesName = "lwxlabs.notifications.firebase.android";
    private const string StateKey = "pending-state";
    private readonly ISharedPreferences _preferences;

    public AndroidFirebaseJournalStorage(Context context) =>
        _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Android preferences are unavailable.");

    public FirebaseAndroidJournalState Read() =>
        JsonSerializer.Deserialize(_preferences.GetString(StateKey, null) ?? "{}", FirebaseJournalJsonContext.Default.FirebaseAndroidJournalState)
        ?? new FirebaseAndroidJournalState();

    public void Write(FirebaseAndroidJournalState state)
    {
        var editor = _preferences.Edit() ?? throw new InvalidOperationException("Android preferences editor is unavailable.");
        editor.PutString(StateKey, JsonSerializer.Serialize(state, FirebaseJournalJsonContext.Default.FirebaseAndroidJournalState));
        if (!editor.Commit()) throw new InvalidOperationException("Could not persist the Firebase event inbox.");
    }
}

[JsonSerializable(typeof(FirebaseAndroidJournalState))]
internal partial class FirebaseJournalJsonContext : JsonSerializerContext;
