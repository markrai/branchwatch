using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class DesktopMemorySnapshotTests
{
    private static readonly Guid Desktop = Guid.Parse("3d58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly Guid Other = Guid.Parse("7c58d290-9c50-4058-bd85-0c039b9c1414");
    private static readonly DateTimeOffset Captured = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProbeRect MainBounds = new(0, 0, 1920, 1080);
    private static readonly ProbeRect SideBounds = new(-1080, 0, 0, 1920);

    [TestMethod]
    public void Capture_RequiresStableActiveDesktopAndPerMonitorCoordinates()
    {
        var report = Report([Window(Desktop, true)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Other, report));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { TopologyStable = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { DesktopHintStable = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { PerMonitorDpiContext = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => DesktopSnapshotCapture.FromReport(Desktop, report with { IncludesIdentities = false }));
    }

    [TestMethod]
    public void Capture_ExcludesOtherDesktopAndUnverifiedWindowsAndKeepsSeparateInstances()
    {
        var first = Window(Desktop, true);
        var spanning = first with
        {
            Handle = "0x2", Title = "Second", EnumerationIndex = 1,
            ScreenBounds = new ProbeRect(-200, 50, 400, 600),
            NormalPlacementBounds = new ProbeRect(-200, 40, 400, 590)
        };
        var report = Report([
            first, spanning,
            first with { Membership = new ProbeDesktopMembership(Other, 0, false, 0) },
            first with { Membership = new ProbeDesktopMembership(Other, 0, true, 0) },
            first with { Membership = new ProbeDesktopMembership(Desktop, 0, false, 0) },
            first with { Classification = "branchwatch" },
            first with { Classification = "auxiliary" },
            first with { NormalPlacementBounds = null },
            first with { Membership = new ProbeDesktopMembership(Desktop, -1, true, 0) }
        ]);
        var snapshot = DesktopSnapshotCapture.FromReport(Desktop, report);

        Assert.HasCount(2, snapshot.Windows);
        Assert.AreNotEqual(snapshot.Windows[0].Id, snapshot.Windows[1].Id);
        Assert.AreEqual(4, snapshot.ExcludedWindowCount);
        CollectionAssert.AreEquivalent(new[] { "main", "side" }, snapshot.Windows[1].IntersectingMonitors.ToArray());
        Assert.AreEqual(spanning.NormalPlacementBounds!.Value, snapshot.Windows[1].NormalPlacementBounds);
        Assert.AreEqual(spanning.ScreenBounds!.Value, snapshot.Windows[1].ScreenBounds);
        Assert.AreEqual<int?>(90, snapshot.Monitors[1].RotationDegrees);
        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.IsFalse(serialized.Contains("\"Handle\"", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("\"ProcessId\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Store_RoundTripsIndependentSnapshotsAndRejectsStaleOrEmptyReplacement()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            var first = Snapshot();
            var other = first with { DesktopId = Other, DesktopName = "Other" };
            store.Save(first, null);
            store.Save(other, null);
            var original = File.ReadAllText(path);
            Assert.HasCount(2, store.Load());
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(first with { CapturedUtc = Captured.AddMinutes(1) }, null));
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Save(first with { Windows = [] }, Captured));
            Assert.AreEqual(original, File.ReadAllText(path));

            var replacement = first with { CapturedUtc = Captured.AddMinutes(1) };
            store.Save(replacement, Captured);
            var restored = new DesktopMemoryStore(path).Load();
            Assert.AreEqual(replacement.CapturedUtc, restored.Single(s => s.DesktopId == Desktop).CapturedUtc);
            Assert.AreEqual(first.Windows[0].Title, restored.Single(s => s.DesktopId == Desktop).Windows[0].Title);
            Assert.AreEqual(first.Monitors[1].Bounds, restored.Single(s => s.DesktopId == Desktop).Monitors[1].Bounds);
            Assert.AreEqual(original, File.ReadAllText(Path.Combine(directory, "snapshots.backup.json")));
            Assert.ThrowsExactly<InvalidOperationException>(() => store.Delete(Desktop, Captured));
            store.Delete(Desktop, replacement.CapturedUtc);
            Assert.AreEqual(Other, store.Load().Single().DesktopId);
        });
    }

    [TestMethod]
    public void Store_CorruptOrUnsupportedFileIsNeverOverwritten()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var store = new DesktopMemoryStore(path);
            File.WriteAllText(path, "{");
            try
            {
                store.Save(Snapshot(), null);
                Assert.Fail("Corrupt snapshot storage was accepted.");
            }
            catch (JsonException) { }
            Assert.AreEqual("{", File.ReadAllText(path));
            const string unsupported = "{\"SchemaVersion\":99,\"Snapshots\":[]}";
            File.WriteAllText(path, unsupported);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Save(Snapshot(), null));
            Assert.AreEqual(unsupported, File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void Protection_NewSnapshotsAndRecoveryBackupContainNoReadableSensitiveFields()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var store = new DesktopMemoryStore(path);
            var snapshot = SensitiveSnapshot();
            Assert.AreEqual(DesktopSnapshotStorageState.Empty, store.GetStorageState());
            store.Save(snapshot, null);
            Assert.AreEqual(DesktopSnapshotStorageState.Protected, store.GetStorageState());
            AssertProtected(path);
            var replacement = snapshot with { CapturedUtc = Captured.AddMinutes(1) };
            store.Save(replacement, Captured);
            AssertProtected(path);
            AssertProtected(backup);
            AssertSensitive(new DesktopMemoryStore(path).Load().Single());
            AssertSensitive(new DesktopMemoryStore(backup).Load().Single());
            store.Delete(Desktop, replacement.CapturedUtc);
            AssertProtected(path);
            AssertProtected(backup);
            Assert.HasCount(0, store.Load());
            Assert.AreEqual(replacement.CapturedUtc, new DesktopMemoryStore(backup).Load().Single().CapturedUtc);
        });
    }

    [TestMethod]
    public void Protection_ExplicitMigrationPreservesBothLegacyGenerationsAndIsIdempotent()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var snapshot = SensitiveSnapshot();
            var previous = snapshot with { CapturedUtc = Captured.AddMinutes(-1) };
            var legacy = JsonSerializer.Serialize(new DesktopSnapshotFile(1, [snapshot]));
            var legacyBackup = JsonSerializer.Serialize(new DesktopSnapshotFile(1, [previous]));
            File.WriteAllText(path, legacy);
            File.WriteAllText(backup, legacyBackup);
            var store = new DesktopMemoryStore(path);
            Assert.AreEqual(DesktopSnapshotStorageState.Legacy, store.GetStorageState());
            AssertSensitive(store.Load().Single());
            Assert.AreEqual(legacy, File.ReadAllText(path));
            Assert.AreEqual(legacyBackup, File.ReadAllText(backup));
            store.ProtectLegacyFiles();
            AssertProtected(path);
            AssertProtected(backup);
            Assert.AreEqual(DesktopSnapshotStorageState.Protected, store.GetStorageState());
            Assert.AreEqual(snapshot.CapturedUtc, store.Load().Single().CapturedUtc);
            Assert.AreEqual(previous.CapturedUtc, new DesktopMemoryStore(backup).Load().Single().CapturedUtc);
            var migrated = File.ReadAllText(path);
            var migratedBackup = File.ReadAllText(backup);
            store.ProtectLegacyFiles();
            Assert.AreEqual(migrated, File.ReadAllText(path));
            Assert.AreEqual(migratedBackup, File.ReadAllText(backup));
            Assert.HasCount(2, Directory.GetFiles(directory));
        });
    }

    [TestMethod]
    public void Protection_MigratesOnSaveAndPreservesPreviousPrimaryInProtectedBackup()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var snapshot = SensitiveSnapshot();
            File.WriteAllText(path, JsonSerializer.Serialize(new DesktopSnapshotFile(1, [snapshot])));
            File.WriteAllText(backup, JsonSerializer.Serialize(new DesktopSnapshotFile(1, [])));
            var store = new DesktopMemoryStore(path);
            store.Save(snapshot with { CapturedUtc = Captured.AddMinutes(1) }, Captured);
            AssertProtected(path);
            AssertProtected(backup);
            Assert.AreEqual(Captured, new DesktopMemoryStore(backup).Load().Single().CapturedUtc);
            AssertSensitive(new DesktopMemoryStore(backup).Load().Single());
        });
    }

    [TestMethod]
    public void Protection_BackupOnlyMigrationDoesNotCreateOrAutomaticallyRestorePrimary()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            File.WriteAllText(backup, JsonSerializer.Serialize(new DesktopSnapshotFile(1, [SensitiveSnapshot()])));
            var store = new DesktopMemoryStore(path);
            Assert.AreEqual(DesktopSnapshotStorageState.Legacy, store.GetStorageState());
            store.ProtectLegacyFiles();
            Assert.IsFalse(File.Exists(path));
            AssertProtected(backup);
            Assert.HasCount(0, store.Load());
            AssertSensitive(new DesktopMemoryStore(backup).Load().Single());
        });
    }

    [TestMethod]
    public void Protection_FailedMigrationEncryptionLeavesBothLegacyFilesUnchanged()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var legacy = JsonSerializer.Serialize(new DesktopSnapshotFile(1, [SensitiveSnapshot()]));
            File.WriteAllText(path, legacy);
            File.WriteAllText(backup, legacy);
            var store = new DesktopMemoryStore(path, new FailingProtector(2));
            Assert.ThrowsExactly<CryptographicException>(() => store.ProtectLegacyFiles());
            Assert.AreEqual(legacy, File.ReadAllText(path));
            Assert.AreEqual(legacy, File.ReadAllText(backup));
            Assert.HasCount(2, Directory.GetFiles(directory));
        });
    }

    [TestMethod]
    public void Protection_FailedDecryptOrInvalidBackupNeverOverwritesExistingFiles()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var snapshot = SensitiveSnapshot();
            var store = new DesktopMemoryStore(path);
            store.Save(snapshot, null);
            var original = File.ReadAllText(path);
            var unavailable = new DesktopMemoryStore(path, new FailingProtector(1));
            Assert.ThrowsExactly<InvalidDataException>(() => unavailable.Load());
            Assert.ThrowsExactly<InvalidDataException>(() => unavailable.Save(snapshot with { CapturedUtc = Captured.AddMinutes(1) }, Captured));
            Assert.AreEqual(original, File.ReadAllText(path));
            File.WriteAllText(backup, "{");
            Assert.Throws<JsonException>(() => store.ProtectLegacyFiles());
            Assert.Throws<JsonException>(() => store.Delete(Desktop, Captured));
            Assert.AreEqual(original, File.ReadAllText(path));
            Assert.AreEqual("{", File.ReadAllText(backup));
            var invalid = JsonSerializer.Serialize(new ProtectedDesktopSnapshotFile(2, "DPAPI-CurrentUser", new byte[32]));
            File.WriteAllText(path, invalid);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
            Assert.AreEqual(invalid, File.ReadAllText(path));
        });
    }

    [TestMethod]
    public void Protection_LegacyUtf8BomRemainsReadableAndMigratable()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var snapshot = SensitiveSnapshot();
            File.WriteAllText(path, JsonSerializer.Serialize(new DesktopSnapshotFile(1, [snapshot])), new UTF8Encoding(true));
            var store = new DesktopMemoryStore(path);
            Assert.AreEqual(DesktopSnapshotStorageState.Legacy, store.GetStorageState());
            AssertSensitive(store.Load().Single());
            store.ProtectLegacyFiles();
            AssertProtected(path);
            AssertSensitive(store.Load().Single());
        });
    }

    [TestMethod]
    public void Protection_ResumeMixedGenerationMigrationRetainsAlreadyProtectedBackup()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var snapshot = SensitiveSnapshot();
            var previous = snapshot with { CapturedUtc = Captured.AddMinutes(-1) };
            File.WriteAllText(path, JsonSerializer.Serialize(new DesktopSnapshotFile(1, [snapshot])));
            new DesktopMemoryStore(backup).Save(previous, null);
            var protectedBackup = File.ReadAllBytes(backup);
            var store = new DesktopMemoryStore(path);
            Assert.AreEqual(DesktopSnapshotStorageState.Legacy, store.GetStorageState());
            AssertSensitive(store.Load().Single());
            store.ProtectLegacyFiles();
            AssertProtected(path);
            Assert.AreEqual(DesktopSnapshotStorageState.Protected, store.GetStorageState());
            CollectionAssert.AreEqual(protectedBackup, File.ReadAllBytes(backup));
            Assert.AreEqual(previous.CapturedUtc, new DesktopMemoryStore(backup).Load().Single().CapturedUtc);
            Assert.AreEqual(snapshot.CapturedUtc, store.Load().Single().CapturedUtc);
        });
    }

    [TestMethod]
    public void Protection_RecoveryCopyRestoresPreviousGenerationWithoutChangingBackup()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var store = new DesktopMemoryStore(path);
            var snapshot = SensitiveSnapshot();
            store.Save(snapshot, null);
            store.Save(snapshot with { CapturedUtc = Captured.AddMinutes(1) }, Captured);
            var expected = File.ReadAllBytes(backup);
            var corrupt = Path.Combine(directory, "damaged-primary.json");
            File.Move(path, corrupt);
            Assert.HasCount(0, new DesktopMemoryStore(path).Load());
            File.Copy(backup, path);
            var recovered = new DesktopMemoryStore(path).Load().Single();
            AssertSensitive(recovered);
            Assert.AreEqual(Captured, recovered.CapturedUtc);
            AssertProtected(path);
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(backup));
            Assert.IsTrue(File.Exists(corrupt));
        });
    }

    [TestMethod]
    public void Protection_TamperedCiphertextIsRejectedWithoutOverwriteOrFallback()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "snapshots.json");
            var backup = Path.Combine(directory, "snapshots.backup.json");
            var snapshot = SensitiveSnapshot();
            var store = new DesktopMemoryStore(path);
            store.Save(snapshot, null);
            store.Save(snapshot with { CapturedUtc = Captured.AddMinutes(1) }, Captured);
            var intactBackup = File.ReadAllBytes(backup);
            var envelope = JsonSerializer.Deserialize<ProtectedDesktopSnapshotFile>(File.ReadAllBytes(path))!;
            envelope.Payload[envelope.Payload.Length / 2] ^= 0x80;
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope));
            var tampered = File.ReadAllBytes(path);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
            Assert.ThrowsExactly<InvalidDataException>(() => store.Save(snapshot, null));
            Assert.ThrowsExactly<InvalidDataException>(() => store.ProtectLegacyFiles());
            CollectionAssert.AreEqual(tampered, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(intactBackup, File.ReadAllBytes(backup));
            AssertSensitive(new DesktopMemoryStore(backup).Load().Single());
        });
    }

    [TestMethod]
    public void CaptureService_CorrelatesOnlyUniqueLiveChromiumWindowAndReverifiesTicket()
    {
        var browser = BrowserProbe();
        var report = Report([browser]);
        var live = new RestoreLiveWindow(browser, new RestoreWindowToken(new IntPtr(1), 1, 2, 12345));
        var inventory = new ExistingRestoreInventory(Desktop, new CurrentDisplayTopology(Captured, Snapshot().Monitors, []), [live]);
        var service = new DesktopMemoryCaptureService(() => report, () => inventory);
        var capture = service.Capture(Desktop);
        var window = capture.Snapshot.Windows.Single();
        var token = capture.Tokens[window.Id];
        Assert.AreEqual(new BrowserNativeToken("0x1", 1, "12345"), token);
        var ticket = new BrowserCaptureTicket("code", Desktop, window.Id, "chrome", token, 0);
        Assert.IsTrue(service.ReverifyBrowser(Desktop, ticket));
        inventory = inventory with { Windows = [live, live] };
        Assert.IsEmpty(service.Capture(Desktop).Tokens);
        Assert.IsFalse(service.ReverifyBrowser(Desktop, ticket));
        inventory = inventory with { Windows = [live with { Token = live.Token! with { CreatedFileTime = 54321 } }] };
        Assert.IsFalse(service.ReverifyBrowser(Desktop, ticket));
        inventory = inventory with { ActiveDesktopId = Other, Windows = [live] };
        Assert.IsEmpty(service.Capture(Desktop).Tokens);
        Assert.IsFalse(service.ReverifyBrowser(Desktop, ticket));
    }

    [TestMethod]
    public void CaptureService_FailedNativeInspectionPreservesLayoutWithoutBrowserTokens()
    {
        var report = Report([BrowserProbe()]);
        var service = new DesktopMemoryCaptureService(() => report,
            () => throw new InvalidOperationException("Synthetic inspection failure"));
        var capture = service.Capture(Desktop);
        Assert.HasCount(1, capture.Snapshot.Windows);
        Assert.IsEmpty(capture.Tokens);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Capture(Other));
    }

    [TestMethod]
    public void CaptureService_FirefoxAndNonBrowserWindowsNeverRequestLiveCorrelation()
    {
        var firefox = BrowserProbe() with
        {
            ExecutablePath = @"C:\Apps\firefox.exe", ProcessName = "firefox", ClassName = "MozillaWindowClass"
        };
        var inspections = 0;
        var service = new DesktopMemoryCaptureService(() => Report([firefox]), () =>
        {
            inspections++;
            throw new InvalidOperationException("Unexpected native browser inspection");
        });
        Assert.IsEmpty(service.Capture(Desktop).Tokens);
        Assert.AreEqual(0, inspections);
    }

    [TestMethod]
    public void Draft_FolderEditPreservesBrowserSelectionAndRejectsStaleReference()
    {
        var capture = DraftCapture();
        var draft = new DesktopMemoryDraft();
        draft.Replace(capture);
        var snapshot = draft.Snapshot!;
        var browser = snapshot.Windows[0];
        var editor = snapshot.Windows[1];
        var ticket = Ticket(capture);
        Assert.IsTrue(draft.TryAttachBrowser(snapshot, ticket, ReviewContext()));
        draft.SetSelected(browser.Id, 1, false);
        var reviewed = draft.Snapshot!;
        var folder = new ApplicationFolderContext(ApplicationFolderKind.VisualStudioCode, @"C:\Projects");
        Assert.IsTrue(draft.TrySetFolder(reviewed, editor.Id, folder));
        Assert.IsFalse(draft.IsSelected(browser.Id, 1));
        Assert.AreEqual(folder, draft.Snapshot!.Windows[1].FolderContext);
        Assert.IsFalse(draft.TrySetFolder(reviewed, editor.Id, null));
        Assert.IsFalse(draft.TryAttachBrowser(reviewed, ticket, ReviewContext()));
        Assert.IsFalse(draft.TrySetFolder(draft.Snapshot!, browser.Id, folder));
    }

    [TestMethod]
    public void Draft_RejectsMismatchedTicketAndRecaptureResetsOnlyReplacedReview()
    {
        var capture = DraftCapture();
        var draft = new DesktopMemoryDraft();
        draft.Replace(capture);
        var snapshot = draft.Snapshot!;
        var ticket = Ticket(capture);
        var browser = snapshot.Windows[0];
        Assert.IsFalse(draft.TryAttachBrowser(snapshot, ticket with { DesktopId = Other }, ReviewContext()));
        Assert.IsFalse(draft.TryAttachBrowser(snapshot, ticket with { Token = ticket.Token with { ProcessCreation = "99" } }, ReviewContext()));
        Assert.IsFalse(draft.TryAttachBrowser(snapshot, ticket, ReviewContext() with { Browser = "firefox" }));
        Assert.IsTrue(draft.TryAttachBrowser(snapshot, ticket, ReviewContext()));
        draft.SetAll(browser.Id, false);
        Assert.IsTrue(draft.TryAttachBrowser(draft.Snapshot!, ticket, ReviewContext()));
        Assert.IsTrue(draft.IsSelected(browser.Id, 0));
        Assert.IsTrue(draft.IsSelected(browser.Id, 1));
        draft.Clear();
        Assert.IsNull(draft.Snapshot);
        Assert.IsFalse(draft.HasBrowserToken(browser.Id));
        Assert.IsFalse(draft.TryAttachBrowser(snapshot, ticket, ReviewContext()));
    }

    [TestMethod]
    public void Draft_SaveFailureRetainsReviewAndSuccessfulSaveClearsTransientState()
    {
        InDirectory(directory =>
        {
            var store = new DesktopMemoryStore(Path.Combine(directory, "snapshots.json"));
            var capture = DraftCapture();
            var draft = new DesktopMemoryDraft();
            draft.Replace(capture);
            var ticket = Ticket(capture);
            Assert.IsTrue(draft.TryAttachBrowser(draft.Snapshot!, ticket, ReviewContext()));
            draft.SetSelected(ticket.WindowId, 1, false);
            var expected = draft.Snapshot!;
            store.Save(capture.Snapshot, null);
            Assert.ThrowsExactly<InvalidOperationException>(() => draft.Save(store, expected, null));
            Assert.AreSame(expected, draft.Snapshot);
            Assert.IsTrue(draft.HasBrowserToken(ticket.WindowId));
            Assert.IsFalse(draft.IsSelected(ticket.WindowId, 1));
            draft.Save(store, expected, capture.Snapshot.CapturedUtc);
            var saved = store.Load().Single();
            Assert.HasCount(1, saved.Windows[0].BrowserContext!.Tabs);
            Assert.AreEqual(ReviewContext().Tabs[0], saved.Windows[0].BrowserContext!.Tabs.Single());
            Assert.IsNull(draft.Snapshot);
            Assert.IsFalse(draft.HasBrowserToken(ticket.WindowId));
        });
    }

    [TestMethod]
    public void Draft_RejectsEmptySaveAndInvalidReplacementWithoutDiscardingExistingDraft()
    {
        InDirectory(directory =>
        {
            var store = new DesktopMemoryStore(Path.Combine(directory, "snapshots.json"));
            var capture = DraftCapture();
            var draft = new DesktopMemoryDraft();
            draft.Replace(capture);
            var invalid = capture with { Tokens = new Dictionary<Guid, BrowserNativeToken> { [Guid.NewGuid()] = Ticket(capture).Token } };
            Assert.ThrowsExactly<InvalidOperationException>(() => draft.Replace(invalid));
            Assert.AreSame(capture.Snapshot, draft.Snapshot);
            var empty = capture.Snapshot with { Windows = [] };
            draft.Replace(new(empty, new Dictionary<Guid, BrowserNativeToken>()));
            Assert.ThrowsExactly<InvalidOperationException>(() => draft.Save(store, empty, null));
            Assert.AreSame(empty, draft.Snapshot);
            Assert.IsEmpty(store.Load());
        });
    }

    private static ProbeWindow BrowserProbe() => Window(Desktop, true) with
    {
        ProcessName = "chrome", ExecutablePath = @"C:\Apps\chrome.exe", ClassName = "Chrome_WidgetWin_1"
    };

    private static DesktopCaptureDraft DraftCapture()
    {
        var snapshot = DesktopSnapshotCapture.FromReport(Desktop, Report([
            BrowserProbe(),
            Window(Desktop, true) with { EnumerationIndex = 1, Handle = "0x2", ProcessId = 2,
                ProcessName = "Code", ExecutablePath = @"C:\Apps\Code.exe", ClassName = "Chrome_WidgetWin_1" }
        ]));
        return new(snapshot, new Dictionary<Guid, BrowserNativeToken> { [snapshot.Windows[0].Id] = new("0x1", 1, "12345") });
    }

    private static BrowserCaptureTicket Ticket(DesktopCaptureDraft capture) =>
        new("code", capture.Snapshot.DesktopId, capture.Snapshot.Windows[0].Id, "chrome", capture.Tokens[capture.Snapshot.Windows[0].Id], 0);

    private static BrowserWindowContext ReviewContext() => new("chrome", "0123456789abcdef0123456789abcdef",
        [new("https://example.com/selected", false, true), new("https://example.com/omitted", false, false)]);

    private sealed class FailingProtector(int failAt) : IDesktopSnapshotProtector
    {
        private readonly CurrentUserSnapshotProtector _real = new();
        private int _calls;
        public byte[] Protect(byte[] data) => ++_calls == failAt ? throw new CryptographicException("Test failure") : _real.Protect(data);
        public byte[] Unprotect(byte[] data) => throw new CryptographicException("Test failure");
    }

    private static DesktopSnapshot SensitiveSnapshot()
    {
        var snapshot = Snapshot();
        var window = snapshot.Windows[0] with
        {
            Title = "sensitive-window-title",
            ProcessName = "chrome",
            ExecutablePath = @"C:\Apps\chrome.exe",
            ClassName = "Chrome_WidgetWin_1",
            BrowserContext = new("chrome", "0123456789abcdef0123456789abcdef",
                [new("https://example.com/private?token=sensitive-url#fragment", false, true)])
        };
        return snapshot with { Windows = [window] };
    }

    private static void AssertProtected(string path)
    {
        var raw = File.ReadAllText(path);
        Assert.IsFalse(raw.Contains("sensitive-url", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("sensitive-window-title", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("0123456789abcdef0123456789abcdef", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(raw);
        Assert.AreEqual(2, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.AreEqual("DPAPI-CurrentUser", doc.RootElement.GetProperty("Protection").GetString());
        Assert.IsNotEmpty(doc.RootElement.GetProperty("Payload").GetBytesFromBase64());
        Assert.IsFalse(doc.RootElement.TryGetProperty("Snapshots", out _));
    }

    private static void AssertSensitive(DesktopSnapshot snapshot)
    {
        var window = snapshot.Windows.Single();
        Assert.AreEqual("sensitive-window-title", window.Title);
        Assert.AreEqual("https://example.com/private?token=sensitive-url#fragment", window.BrowserContext!.Tabs.Single().Url);
    }

    private static DesktopSnapshot Snapshot() => DesktopSnapshotCapture.FromReport(Desktop, Report([Window(Desktop, true)]));

    private static DesktopMemoryProbeReport Report(IReadOnlyList<ProbeWindow> windows)
    {
        var state = new ProbeDesktopState(Desktop, [new ProbeDesktop(Desktop, "Work"), new ProbeDesktop(Other, "Other")]);
        ProbeMonitor[] monitors =
        [
            new("main", MainBounds, MainBounds, true, 0, 1920, 1080, 60, 100, 0, [new ProbeDisplayDevice("main-id", 1, "Main")]),
            new("side", SideBounds, SideBounds, false, 90, 1080, 1920, 60, 125, 0, [new ProbeDisplayDevice("side-id", 1, "Side")])
        ];
        return new DesktopMemoryProbeReport(1, Captured.AddSeconds(-1), Captured, "Windows", "X64", true, true,
            state, state, monitors, monitors, true, true, windows, []);
    }

    private static ProbeWindow Window(Guid desktop, bool current) => new(
        Handle: "0x1", ProcessId: 1, ProcessName: "Editor", ExecutablePath: @"C:\Apps\Editor.exe",
        Title: "First", ClassName: "EditorWindow", Visible: true, Owned: false, ToolWindow: false,
        AppWindow: true, Minimized: false, Maximized: false, CloakFlags: 0, CloakHResult: 0, StyleError: null,
        ScreenBounds: new ProbeRect(50, 50, 700, 600), BoundsError: null, VisibleFrameBounds: null, FrameHResult: 0,
        NormalPlacementBounds: new ProbeRect(50, 40, 700, 590), PlacementError: null, ShowCommand: 1, PlacementFlags: 0,
        MonitorSource: "main", WindowDpi: 96, Membership: new ProbeDesktopMembership(desktop, 0, current, 0),
        Classification: "candidate", EnumerationIndex: 0);

    private static void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
