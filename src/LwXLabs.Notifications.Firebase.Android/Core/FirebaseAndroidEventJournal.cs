using System;
using System.Collections.Generic;
using System.Linq;

namespace LwXLabs.Notifications.Firebase.Android;

internal sealed class FirebaseAndroidProcessState
{
    public static FirebaseAndroidProcessState Shared { get; } = new();
    public HashSet<string> ObservedInstallationIds { get; } = new(StringComparer.Ordinal);
}

internal sealed class FirebaseAndroidEventJournal : IFirebaseAndroidEventStore
{
    private static readonly object Gate = new();
    private readonly IFirebaseAndroidJournalStorage _storage;
    private readonly Action<string> _warning;
    private readonly FirebaseAndroidProcessState _processState;

    public FirebaseAndroidEventJournal(IFirebaseAndroidJournalStorage storage, Action<string> warning,
        FirebaseAndroidProcessState? processState = null)
    {
        _storage = storage;
        _warning = warning;
        _processState = processState ?? FirebaseAndroidProcessState.Shared;
    }

    public void RecordInstallationId(string installationId, bool confirmCurrent = false)
    {
        if (string.IsNullOrWhiteSpace(installationId)) return;
        lock (Gate)
        {
            var state = _storage.Read();
            var unchanged = string.Equals(state.LastInstallationId, installationId, StringComparison.Ordinal);
            if (unchanged && (!confirmCurrent || _processState.ObservedInstallationIds.Contains(installationId)
                || state.Pending.Any(x => x.Registration?.InstallationId == installationId)))
            {
                if (confirmCurrent) _processState.ObservedInstallationIds.Add(installationId);
                return;
            }
            state.Pending.Add(new PendingFirebaseEvent(++state.NextSequence,
                new FirebaseAndroidRegistrationChange(installationId, state.LastInstallationId == null
                    ? FirebaseAndroidRegistrationChangeKind.Obtained
                    : unchanged ? FirebaseAndroidRegistrationChangeKind.Confirmed
                    : FirebaseAndroidRegistrationChangeKind.Refreshed), null));
            state.LastInstallationId = installationId;
            _storage.Write(state);
            _processState.ObservedInstallationIds.Add(installationId);
        }
    }

    public void RecordMessage(FirebaseAndroidMessage message)
    {
        lock (Gate)
        {
            var state = _storage.Read();
            if (state.Pending.Count(x => x.Message != null) >= 100)
            {
                state.Pending.RemoveAt(state.Pending.FindIndex(x => x.Message != null));
                try { _warning("FCM inbox full; discarded the oldest pending message."); } catch { }
            }
            state.Pending.Add(new PendingFirebaseEvent(++state.NextSequence, null, message));
            _storage.Write(state);
        }
    }

    public PendingFirebaseEvent? Peek()
    {
        lock (Gate) return _storage.Read().Pending.FirstOrDefault();
    }

    public void Remove(long sequence)
    {
        lock (Gate)
        {
            var state = _storage.Read();
            if (state.Pending.Count == 0 || state.Pending[0].Sequence != sequence) return;
            state.Pending.RemoveAt(0);
            _storage.Write(state);
        }
    }
}
