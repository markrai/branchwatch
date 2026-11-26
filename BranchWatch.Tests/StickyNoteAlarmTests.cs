using System.IO;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class StickyNoteAlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void OneShot_FiresAtDeadlineAndStaysLatchedUntilDismissed()
    {
        var note = new StickyNote();
        var other = new StickyNote();
        Assert.IsTrue(note.SetAlarm(Now.AddMinutes(1), Now));
        Assert.IsFalse(note.AdvanceAlarm(Now));
        Assert.IsFalse(note.IsAlarmRinging);
        Assert.IsTrue(note.AdvanceAlarm(Now.AddMinutes(1)));
        Assert.IsTrue(note.IsAlarmRinging);
        Assert.IsFalse(note.AdvanceAlarm(Now.AddDays(1)));
        Assert.IsFalse(note.AdvanceAlarm(Now.AddHours(-1)));
        Assert.IsTrue(note.IsAlarmRinging);
        Assert.IsFalse(other.IsAlarmRinging);
        Assert.IsTrue(note.ClearAlarm());
        Assert.IsFalse(note.IsAlarmRinging);
        Assert.IsNull(note.AlarmDueUtc);
        Assert.IsFalse(note.AdvanceAlarm(Now.AddYears(1)));
        Assert.IsFalse(note.ClearAlarm());
    }

    [TestMethod]
    public void Reschedule_ReplacesPriorAlarmAndRejectsPastWithoutChangingIt()
    {
        var note = new StickyNote();
        Assert.IsTrue(note.SetAlarm(Now.AddMinutes(1), Now));
        Assert.IsTrue(note.AdvanceAlarm(Now.AddMinutes(1)));
        Assert.IsFalse(note.SetAlarm(Now, Now));
        Assert.IsTrue(note.IsAlarmRinging);
        var next = Now.AddDays(2).ToOffset(TimeSpan.FromHours(-5));
        Assert.IsTrue(note.SetAlarm(next, Now));
        Assert.IsFalse(note.IsAlarmRinging);
        Assert.AreEqual(next.ToUniversalTime(), note.AlarmDueUtc);
        Assert.AreEqual(TimeSpan.Zero, note.AlarmDueUtc!.Value.Offset);
        Assert.IsFalse(note.AdvanceAlarm(Now.AddDays(1)));
        Assert.IsTrue(note.AdvanceAlarm(next));
    }

    [TestMethod]
    public void Persistence_OverdueAndDismissedAlarmsSurviveRestartAndLegacyNotesHaveNone()
    {
        var path = Path.GetTempFileName();
        try
        {
            var desktop = Guid.NewGuid();
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { new { Id = Guid.NewGuid(), DesktopId = desktop } }));
            var store = new StickyNoteStore(path);
            var note = store.Load().Single();
            Assert.IsNull(note.AlarmDueUtc);
            Assert.IsFalse(note.AlarmTriggered);
            note.IsOpen = false;
            note.SetAlarm(Now.AddMinutes(1), Now);
            store.Save([note]);
            var loaded = store.Load().Single();
            Assert.IsFalse(loaded.IsAlarmRinging);
            Assert.IsTrue(loaded.AdvanceAlarm(Now.AddHours(1)));
            store.Save([loaded]);
            loaded = store.Load().Single();
            Assert.IsTrue(loaded.IsAlarmRinging);
            Assert.IsFalse(loaded.IsOpen);
            Assert.IsTrue(loaded.ClearAlarm());
            store.Save([loaded]);
            loaded = store.Load().Single();
            Assert.IsFalse(loaded.AdvanceAlarm(Now.AddDays(1)));
            Assert.IsNull(loaded.AlarmDueUtc);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void LocalTime_RejectsPastAndResolvesFutureWithExplicitOffset()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test-05", TimeSpan.FromHours(-5), "Test", "Test");
        Assert.IsTrue(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 11, 26), 10, 1, zone, Now, out var due, out var error));
        Assert.AreEqual(Now.AddMinutes(1), due);
        Assert.AreEqual("", error);
        Assert.IsFalse(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 11, 26), 10, 0, zone, Now, out _, out _));
        Assert.IsFalse(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 11, 26), 24, 0, zone, Now, out _, out _));
        Assert.AreEqual(new DateTime(2026, 11, 27), StickyNoteAlarmTime.NextMinute(new DateTime(2026, 11, 26, 23, 59, 30)));
    }

    [TestMethod]
    public void LocalTime_RejectsNonexistentAndAmbiguousDaylightSavingTimes()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var before = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.IsFalse(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 3, 8), 2, 30, zone, before, out _, out _));
        Assert.IsFalse(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 11, 1), 1, 30, zone, before, out _, out _));
        Assert.IsTrue(StickyNoteAlarmTime.TryResolve(new DateTime(2026, 11, 1), 3, 30, zone, before, out var due, out _));
        Assert.AreEqual(new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero), due);
    }
}
