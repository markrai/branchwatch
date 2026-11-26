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

    private static void WithController(Action<StickyNotesController, StickyNoteStore, TestDesktopManager> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
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
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try { if (failure is not null) throw failure; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class TestDesktopManager : IStickyNoteDesktopManager
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal bool Disposed { get; private set; }
        internal StickyNoteWindow Window { get; private set; } = null!;

        public Guid? GetDesktop(IntPtr window)
        {
            if (window == IntPtr.Zero || Disposed) return null;
            if (HwndSource.FromHwnd(window)?.RootVisual is StickyNoteWindow note) Window = note;
            return Id;
        }

        public bool IsCurrent(IntPtr window) => window != IntPtr.Zero && !Disposed;
        public bool Move(IntPtr window, Guid target) => !Disposed && target == Id;
        public void Dispose() => Disposed = true;
    }
}
