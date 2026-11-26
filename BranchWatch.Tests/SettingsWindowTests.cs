using System.IO;
using System.Windows;

namespace BranchWatch.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsWindowTests
{
    [TestMethod]
    public void Host_ReusesWindowPreservesDraftAndCannotReopenAfterDisposal()
    {
        Exception? failure = null;
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var service = new SettingsService(Path.Combine(directory, "settings.json"));
                using var desktops = new VirtualDesktopOverlayController(service, settings);
                using var notes = new StickyNotesController(desktops.Monitor, Path.Combine(directory, "notes.json"), settings);
                var created = new List<SettingsWindow>();
                using var host = new SettingsWindowHost(() =>
                {
                    var window = new SettingsWindow(service, settings, notes, desktops, () => { }, () => { },
                        () => { }, () => { }, () => { }, () => { });
                    created.Add(window);
                    return window;
                });
                host.Open(SettingsSection.StickyNotes);
                var first = created.Single();
                var page = (StickyNoteSettingsPage)first.NotesHost.Content;
                page.TransparencySlider.Value = 8;
                first.WindowState = WindowState.Minimized;
                host.Open(SettingsSection.General);
                Assert.AreEqual(WindowState.Normal, first.WindowState);
                host.Open(SettingsSection.StickyNotes);
                Assert.HasCount(1, created);
                Assert.AreEqual(8.0, page.TransparencySlider.Value);
                first.Close();
                host.Open(SettingsSection.DesktopMemory);
                Assert.HasCount(2, created);
                Assert.AreEqual(Visibility.Visible, created[1].MemoryPage.Visibility);
                host.Dispose();
                host.Open(SettingsSection.General);
                host.RefreshRepositoryState();
                host.RefreshExternalSettings();
                Assert.HasCount(2, created);
                Assert.IsFalse(created[1].IsVisible);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try { if (failure is not null) throw failure; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Navigate_SelectsExactlyOnePageAndPreservesStickyNoteDraft()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                var settings = new AppSettings();
                var service = new SettingsService(Path.Combine(directory, "settings.json"));
                using var desktops = new VirtualDesktopOverlayController(service, settings);
                using var notes = new StickyNotesController(desktops.Monitor, Path.Combine(directory, "notes.json"), settings);
                var window = new SettingsWindow(service, settings, notes, desktops, () => { }, () => { },
                    () => { }, () => { }, () => { }, () => { });
                try
                {
                    var pages = new[] { window.GeneralPage, window.GitPage, window.DesktopsPage, window.MemoryPage, window.NotesPage, window.AboutPage };
                    foreach (var section in Enum.GetValues<SettingsSection>())
                    {
                        window.Navigate(section);
                        Assert.AreEqual((int)section, window.Navigation.SelectedIndex);
                        Assert.AreEqual(1, pages.Count(page => page.Visibility == Visibility.Visible));
                        Assert.AreEqual(Visibility.Visible, pages[(int)section].Visibility);
                    }

                    window.Navigate(SettingsSection.DesktopMemory);
                    var memory = (DesktopMemorySettingsPage)window.MemoryHost.Content;
                    Assert.AreEqual(Visibility.Collapsed, memory.PlanCard.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, memory.RestoreResultsCard.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, memory.RestoreProgress.Visibility);
                    Assert.IsFalse(memory.RestoreButton.IsEnabled);
                    Assert.AreEqual(Visibility.Collapsed, memory.BrowserEditor.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, memory.BrowserReviewSummary.Visibility);
                    Assert.AreEqual(Visibility.Collapsed, memory.BrowserRestoreControls.Visibility);
                    Assert.IsFalse(memory.LaunchMissingCheck.IsChecked ?? true);
                    window.Navigate(SettingsSection.StickyNotes);
                    var page = (StickyNoteSettingsPage)window.NotesHost.Content;
                    page.TransparencySlider.Value = 8;
                    page.ColorBox.SelectedValue = "Blue";
                    window.Navigate(SettingsSection.General);
                    window.Navigate(SettingsSection.StickyNotes);
                    Assert.AreEqual(8.0, page.TransparencySlider.Value);
                    Assert.AreEqual("Blue", page.ColorBox.SelectedValue);
                    Assert.AreEqual(3, settings.StickyNoteDefaultTransparencyLevel);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
