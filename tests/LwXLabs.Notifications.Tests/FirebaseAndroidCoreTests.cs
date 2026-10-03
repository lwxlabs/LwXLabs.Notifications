using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LwXLabs.Notifications.Firebase.Android;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LwXLabs.Notifications.Tests;

public sealed class FirebaseAndroidCoreTests
{
    [Fact]
    public void Di_registration_is_idempotent_and_resolves_one_pipeline()
    {
        var services = new ServiceCollection();
        var platform = new FakePlatform("first");
        var storage = new MemoryStorage();
        services.AddSingleton<IFirebaseAndroidPlatform>(platform);
        services.AddSingleton<IFirebaseAndroidJournalStorage>(storage);
        services.AddSingleton<IFirebaseAndroidNotificationHandler>(new RecordingHandler());
        FirebaseAndroidCoreRegistration.AddCore(services, _ => { }, _ => { });
        FirebaseAndroidCoreRegistration.AddCore(services, _ => { }, _ => { });

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<FirebaseAndroidPipeline>());
        Assert.Single(provider.GetServices<IFirebaseAndroidEventStore>());
        Assert.Same(provider.GetRequiredService<FirebaseAndroidPipeline>(), provider.GetRequiredService<FirebaseAndroidPipeline>());
    }

    [Fact]
    public void Registration_journal_records_first_and_changed_ids_once_and_replays_from_storage()
    {
        var storage = new MemoryStorage();
        var firstProcess = new FirebaseAndroidEventJournal(storage, _ => { });
        firstProcess.RecordInstallationId("first");
        firstProcess.RecordInstallationId("first");
        firstProcess.RecordInstallationId("second");

        var nextProcess = new FirebaseAndroidEventJournal(storage, _ => { });
        var obtained = nextProcess.Peek()!;
        Assert.Equal("first", obtained.Registration!.InstallationId);
        Assert.Equal(FirebaseAndroidRegistrationChangeKind.Obtained, obtained.Registration.Kind);
        nextProcess.Remove(obtained.Sequence);
        var refreshed = nextProcess.Peek()!;
        Assert.Equal("second", refreshed.Registration!.InstallationId);
        Assert.Equal(FirebaseAndroidRegistrationChangeKind.Refreshed, refreshed.Registration.Kind);
        nextProcess.Remove(refreshed.Sequence);
        Assert.Null(nextProcess.Peek());
    }

    [Fact]
    public void Message_normalization_copies_data_and_keeps_notification_fields()
    {
        var input = new Dictionary<string, string> { ["action"] = "open", ["lwx_id"] = "id-1" };
        var message = FirebaseAndroidMessageNormalizer.Normalize("msg", "sender", "collapse", 123L, 60,
            "Title", "Body", input);
        input["action"] = "changed";

        Assert.Equal("msg", message.MessageId);
        Assert.Equal("sender", message.Sender);
        Assert.Equal("collapse", message.CollapseKey);
        Assert.Equal(123L, message.SentTimeMilliseconds);
        Assert.Equal(60, message.TimeToLiveSeconds);
        Assert.Equal("Title", message.Title);
        Assert.Equal("Body", message.Body);
        Assert.Equal("open", message.Data["action"]);
    }

    [Fact]
    public async Task Repeated_concurrent_initialization_uses_one_platform_registration_and_one_callback()
    {
        var platform = new FakePlatform("fid");
        var handler = new RecordingHandler();
        var journal = new FirebaseAndroidEventJournal(new MemoryStorage(), _ => { });
        var pipeline = new FirebaseAndroidPipeline(platform, journal, new[] { handler }, _ => { });

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pipeline.InitializeAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(1, platform.InitializeCount);
        Assert.Equal(1, platform.RegisterCount);
        Assert.Single(handler.Registrations);
        Assert.Equal(FirebaseAndroidRegistrationChangeKind.Obtained, handler.Registrations[0].Kind);
    }

    [Fact]
    public async Task New_process_confirms_unchanged_installation_id_for_backend_resync()
    {
        var storage = new MemoryStorage();
        var firstHandler = new RecordingHandler();
        var first = new FirebaseAndroidPipeline(new FakePlatform("fid"),
            new FirebaseAndroidEventJournal(storage, _ => { }, new FirebaseAndroidProcessState()), new[] { firstHandler }, _ => { });
        await first.InitializeAsync(TestContext.Current.CancellationToken);

        var nextHandler = new RecordingHandler();
        var next = new FirebaseAndroidPipeline(new FakePlatform("fid"),
            new FirebaseAndroidEventJournal(storage, _ => { }, new FirebaseAndroidProcessState()), new[] { nextHandler }, _ => { });
        await next.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Single(nextHandler.Registrations);
        Assert.Equal(FirebaseAndroidRegistrationChangeKind.Confirmed, nextHandler.Registrations[0].Kind);
    }

    [Fact]
    public async Task Native_registration_during_startup_does_not_dispatch_a_duplicate_confirmation()
    {
        var storage = new MemoryStorage();
        var process = new FirebaseAndroidProcessState();
        var nativeService = new FirebaseAndroidEventJournal(storage, _ => { }, process);
        nativeService.RecordInstallationId("fid", confirmCurrent: true);
        var handler = new RecordingHandler();
        var pipeline = new FirebaseAndroidPipeline(new FakePlatform("fid"),
            new FirebaseAndroidEventJournal(storage, _ => { }, process), new[] { handler }, _ => { });

        await pipeline.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Single(handler.Registrations);
    }

    [Fact]
    public async Task Explicit_installation_id_read_returns_current_id_without_duplicate_callback()
    {
        var handler = new RecordingHandler();
        var pipeline = new FirebaseAndroidPipeline(new FakePlatform("fid"),
            new FirebaseAndroidEventJournal(new MemoryStorage(), _ => { }), new[] { handler }, _ => { });

        var id = await pipeline.GetInstallationIdAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fid", id);
        Assert.Single(handler.Registrations);
    }

    [Fact]
    public async Task Failed_callback_is_isolated_and_pending_event_is_replayed()
    {
        var errors = new List<Exception>();
        var handler = new RecordingHandler { FailNextRegistration = true };
        var journal = new FirebaseAndroidEventJournal(new MemoryStorage(), _ => { });
        var pipeline = new FirebaseAndroidPipeline(new FakePlatform("fid"), journal, new[] { handler }, errors.Add);

        await pipeline.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Single(errors);
        Assert.NotNull(journal.Peek());

        await pipeline.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(journal.Peek());
        Assert.Single(handler.Registrations);
    }

    [Fact]
    public async Task Background_message_saved_before_initialization_reaches_handler_after_bootstrap()
    {
        var storage = new MemoryStorage();
        var beforeBootstrap = new FirebaseAndroidEventJournal(storage, _ => { });
        beforeBootstrap.RecordMessage(FirebaseAndroidMessageNormalizer.Normalize("msg-1", null, null, 1, 30, null, null,
            new[] { new KeyValuePair<string, string>("key", "value") }));
        var handler = new RecordingHandler();
        var pipeline = new FirebaseAndroidPipeline(new FakePlatform("fid"),
            new FirebaseAndroidEventJournal(storage, _ => { }), new[] { handler }, _ => { });

        await pipeline.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Single(handler.Messages);
        Assert.Equal("msg-1", handler.Messages[0].MessageId);
        Assert.Equal("value", handler.Messages[0].Data["key"]);
        Assert.Single(handler.Registrations);
    }

    [Fact]
    public void Bounded_message_queue_never_discards_pending_registration()
    {
        var warnings = new List<string>();
        var journal = new FirebaseAndroidEventJournal(new MemoryStorage(), warnings.Add);
        journal.RecordInstallationId("fid");
        for (var i = 0; i < 101; i++)
            journal.RecordMessage(FirebaseAndroidMessageNormalizer.Normalize(i.ToString(), null, null, 0, 0, null, null, null));

        Assert.Equal("fid", journal.Peek()!.Registration!.InstallationId);
        Assert.Single(warnings);
    }

    private sealed class MemoryStorage : IFirebaseAndroidJournalStorage
    {
        private FirebaseAndroidJournalState _state = new();
        public FirebaseAndroidJournalState Read() => Copy(_state);
        public void Write(FirebaseAndroidJournalState state) => _state = Copy(state);
        private static FirebaseAndroidJournalState Copy(FirebaseAndroidJournalState state) => new()
        {
            LastInstallationId = state.LastInstallationId,
            NextSequence = state.NextSequence,
            Pending = state.Pending.ToList()
        };
    }

    private sealed class FakePlatform(string installationId) : IFirebaseAndroidPlatform
    {
        public int InitializeCount { get; private set; }
        public int RegisterCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) { InitializeCount++; return Task.CompletedTask; }
        public Task<string> RegisterAndGetInstallationIdAsync(CancellationToken cancellationToken)
        { RegisterCount++; return Task.FromResult(installationId); }
        public Task<string> GetInstallationIdAsync(CancellationToken cancellationToken) => Task.FromResult(installationId);
    }

    private sealed class RecordingHandler : IFirebaseAndroidNotificationHandler
    {
        public bool FailNextRegistration { get; set; }
        public List<FirebaseAndroidRegistrationChange> Registrations { get; } = new();
        public List<FirebaseAndroidMessage> Messages { get; } = new();

        public Task OnRegistrationAsync(FirebaseAndroidRegistrationChange change, CancellationToken cancellationToken = default)
        {
            if (FailNextRegistration) { FailNextRegistration = false; throw new InvalidOperationException("temporary"); }
            Registrations.Add(change);
            return Task.CompletedTask;
        }

        public Task OnMessageAsync(FirebaseAndroidMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
