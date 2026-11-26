using System.IO;
using System.Windows.Interop;

namespace BranchWatch.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StickyNoteLifecycleTests
{
    [TestMethod]
    public void Dispose_PersistsPendingRenameAndPreservesOpenState()
    {
        WithController((controller, store, desktop) =>
        {
            var note = controller.Notes.Single(n => n.IsOpen);
            controller.OpenOnCurrentDesktop(note);
            var window = desktop.Window;
            window.BeginRename();
            window.TitleEditor.Text = "Pending shutdown title";
            Assert.AreEqual("Original", store.Load().Single(n => n.Id == note.Id).CustomTitle);
            controller.Dispose();
            var saved = store.Load();
            var restored = saved.Single(n => n.Id == note.Id);
            Assert.AreEqual("Pending shutdown title", restored.CustomTitle);
            Assert.AreEqual("Original body", restored.Text);
            Assert.IsTrue(restored.IsOpen);
            Assert.AreEqual(note.DesktopId, restored.DesktopId);
            Assert.AreEqual(note.AlarmDueUtc, restored.AlarmDueUtc);
            Assert.IsFalse(saved.Single(n => n.Id != note.Id).IsOpen);
            Assert.IsFalse(controller.IsAvailable);
            Assert.IsTrue(desktop.Disposed);
            controller.Dispose();
        });
    }

    [TestMethod]
    public void Dispose_DoesNotRestoreCancelledRenameOrDismissedAlarm()
    {
        WithController((controller, store, desktop) =>
        {
            var note = controller.Notes.Single(n => n.IsOpen);
            controller.OpenOnCurrentDesktop(note);
            var window = desktop.Window;
            window.BeginRename();
            window.TitleEditor.Text = "Cancelled title";
            window.FinishRename(commit: false);
            window.Editor.Text = "Final body";
            note.ClearAlarm();
            controller.AlarmChanged();
            controller.Dispose();
            var saved = store.Load().Single(n => n.Id == note.Id);
            Assert.AreEqual("Original", saved.CustomTitle);
            Assert.AreEqual("Final body", saved.Text);
            Assert.IsNull(saved.AlarmDueUtc);
            Assert.IsFalse(saved.AlarmTriggered);
            Assert.IsTrue(saved.IsOpen);
        });
    }

    [TestMethod]
    public void ExplicitClose_PersistsRenameAndClosedStateInsteadOfRestoringOnRestart()
    {
        WithController((controller, store, desktop) =>
        {
            var note = controller.Notes.Single(n => n.IsOpen);
            controller.OpenOnCurrentDesktop(note);
            var window = desktop.Window;
            window.BeginRename();
            window.TitleEditor.Text = "Closed title";
            window.Close();
            var saved = store.Load().Single(n => n.Id == note.Id);
            Assert.AreEqual("Closed title", saved.CustomTitle);
            Assert.IsFalse(saved.IsOpen);
            controller.Dispose();
            Assert.IsFalse(store.Load().Single(n => n.Id == note.Id).IsOpen);
        });
    }

    [TestMethod]
    public void Start_IsIdempotentAndCannotReviveDisposedController()
    {
        WithController((controller, store, desktop) =>
        {
            var note = controller.Notes.Single(n => n.IsOpen);
            controller.OpenOnCurrentDesktop(note);
            desktop.Window.Editor.Text = "Unsaved edit";
            controller.Start();
            Assert.AreSame(note, controller.Notes.Single(n => n.IsOpen));
            Assert.AreEqual("Unsaved edit", note.Text);
            controller.Dispose();
            controller.Start();
            Assert.IsFalse(controller.IsAvailable);
            Assert.IsTrue(desktop.Disposed);
            Assert.AreEqual("Unsaved edit", store.Load().Single(n => n.Id == note.Id).Text);
        });
    }

    [TestMethod]
    public void Maintenance_EmptyAndClosedNotesWithoutAlarmsRemainIdle()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "notes.json");
            using var monitor = new VirtualDesktopMonitor();
            using var controller = new StickyNotesController(monitor, path);
            controller.Start();
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            controller.ScheduleRestore(Guid.NewGuid());
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            controller.MarkDirty();
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            controller.Dispose();
            controller.MarkDirty();
            Assert.IsFalse(controller.IsMaintenanceScheduled);
        });
    }

    [TestMethod]
    public void Maintenance_ClosedAlarmFiresPersistsAndStopsUntilRescheduled()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "notes.json");
            var store = new StickyNoteStore(path);
            var now = DateTimeOffset.UtcNow;
            var due = now.AddDays(1);
            store.Save([new StickyNote { DesktopId = Guid.NewGuid(), IsOpen = false, AlarmDueUtc = due }]);
            using var monitor = new VirtualDesktopMonitor();
            using var controller = new StickyNotesController(monitor, path);
            controller.Start();
            var note = controller.Notes.Single();
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(due.AddSeconds(-1));
            Assert.IsFalse(note.IsAlarmRinging);
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(due);
            Assert.IsTrue(note.IsAlarmRinging);
            Assert.IsTrue(store.Load().Single().IsAlarmRinging);
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            Assert.IsTrue(note.SetAlarm(due.AddDays(1), due));
            controller.AlarmChanged();
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            Assert.IsTrue(note.ClearAlarm());
            controller.AlarmChanged();
            Assert.IsFalse(controller.IsMaintenanceScheduled);
        });
    }

    [TestMethod]
    public void Maintenance_UnsavedChangesRetryUntilPersistenceSucceeds()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "notes.json");
            Directory.CreateDirectory(directory);
            using var monitor = new VirtualDesktopMonitor();
            using var controller = new StickyNotesController(monitor, path);
            controller.Start();
            Directory.CreateDirectory(path);
            try
            {
                controller.AlarmChanged();
                Assert.IsTrue(controller.IsMaintenanceScheduled);
                controller.RunMaintenance(DateTimeOffset.UtcNow);
                Assert.IsTrue(controller.IsMaintenanceScheduled);
            }
            finally { Directory.Delete(path); }
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(controller.IsMaintenanceScheduled);
        });
    }

    [TestMethod]
    public void Maintenance_RestoreRetriesAndOpenWindowKeepTimerAlive()
    {
        WithController((controller, store, desktop) =>
        {
            var note = controller.Notes.Single(n => n.IsOpen);
            note.ClearAlarm();
            controller.AlarmChanged();
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            desktop.RejectWindows = true;
            controller.ScheduleRestore(desktop.Id);
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            Assert.IsTrue(note.IsOpen);

            desktop.RejectWindows = false;
            controller.ScheduleRestore(desktop.Id);
            controller.RunMaintenance(DateTimeOffset.UtcNow);
            Assert.IsTrue(controller.IsMaintenanceScheduled);
            desktop.Window.Close();
            Assert.IsFalse(controller.IsMaintenanceScheduled);
            Assert.IsFalse(store.Load().Single(n => n.Id == note.Id).IsOpen);
        });
    }

    private static void WithController(Action<StickyNotesController, StickyNoteStore, TestDesktopManager> action)
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var desktop = new TestDesktopManager();
            var path = Path.Combine(directory, "notes.json");
            var store = new StickyNoteStore(path);
            store.Save([
                new StickyNote { DesktopId = desktop.Id, CustomTitle = "Original", Text = "Original body",
                    TransparencyLevel = 1, AlarmDueUtc = DateTimeOffset.UtcNow.AddDays(1) },
                new StickyNote { DesktopId = desktop.Id, IsOpen = false, Text = "Closed note" }
            ]);
            using var monitor = new VirtualDesktopMonitor();
            using var controller = new StickyNotesController(monitor, path, native: desktop);
            controller.Start();
            action(controller, store, desktop);
        });
    }

    private sealed class TestDesktopManager : IStickyNoteDesktopManager
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal bool Disposed { get; private set; }
        internal bool RejectWindows { get; set; }
        internal StickyNoteWindow Window { get; private set; } = null!;

        public Guid? GetDesktop(IntPtr window)
        {
            if (window == IntPtr.Zero || Disposed || RejectWindows) return null;
            if (HwndSource.FromHwnd(window)?.RootVisual is StickyNoteWindow note) Window = note;
            return Id;
        }

        public bool IsCurrent(IntPtr window) => window != IntPtr.Zero && !Disposed;
        public bool Move(IntPtr window, Guid target) => !Disposed && target == Id;
        public void Dispose() => Disposed = true;
    }
}
