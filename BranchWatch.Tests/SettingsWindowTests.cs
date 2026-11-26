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
        StaTest.WithTemporaryDirectory(directory =>
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
        });
    }

    [TestMethod]
    public void Navigate_SelectsExactlyOnePageAndPreservesStickyNoteDraft()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
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
        });
    }

    [TestMethod]
    public void GeneralAndExternalRefresh_DoNotConstructUnvisitedPages()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var settings = new AppSettings();
            var service = new SettingsService(Path.Combine(directory, "settings.json"));
            using var desktops = new VirtualDesktopOverlayController(service, settings);
            using var notes = new StickyNotesController(desktops.Monitor, Path.Combine(directory, "notes.json"), settings);
            var window = new SettingsWindow(service, settings, notes, desktops, () => { }, () => { },
                () => { }, () => { }, () => { }, () => { });
            try
            {
                window.RefreshRepositoryState();
                window.RefreshExternalSettings();
                window.Navigate(SettingsSection.About);
                window.Navigate(SettingsSection.General);
                Assert.IsNull(window.GitOverlayHost.Content);
                Assert.IsNull(window.DesktopOverlayHost.Content);
                Assert.IsNull(window.MemoryHost.Content);
                Assert.IsNull(window.NotesHost.Content);

                settings.OverlayScale = 0.75;
                window.Navigate(SettingsSection.GitWatcher);
                var git = (GitOverlaySettingsPage)window.GitOverlayHost.Content;
                Assert.AreEqual(0.75, git.SizeSlider.Value);
                Assert.IsNull(window.DesktopOverlayHost.Content);
                Assert.IsNull(window.MemoryHost.Content);
                Assert.IsNull(window.NotesHost.Content);

                settings.VirtualDesktopOverlayVisible = false;
                window.RefreshExternalSettings();
                Assert.IsNull(window.DesktopOverlayHost.Content);
                window.Navigate(SettingsSection.VirtualDesktops);
                var desktop = (DesktopOverlaySettingsPage)window.DesktopOverlayHost.Content;
                Assert.AreNotEqual(true, desktop.ShowOverlayCheckBox.IsChecked);
                settings.VirtualDesktopOverlayVisible = true;
                window.RefreshExternalSettings();
                Assert.AreEqual(true, desktop.ShowOverlayCheckBox.IsChecked);
                window.Navigate(SettingsSection.GitWatcher);
                Assert.AreSame(git, window.GitOverlayHost.Content);
                window.Navigate(SettingsSection.VirtualDesktops);
                Assert.AreSame(desktop, window.DesktopOverlayHost.Content);
                Assert.IsNull(window.MemoryHost.Content);
                Assert.IsNull(window.NotesHost.Content);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Close_StopsMemoryPageAndReleasesAllVisitedContent()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var settings = new AppSettings();
            var service = new SettingsService(Path.Combine(directory, "settings.json"));
            using var desktops = new VirtualDesktopOverlayController(service, settings);
            using var notes = new StickyNotesController(desktops.Monitor, Path.Combine(directory, "notes.json"), settings);
            var window = new SettingsWindow(service, settings, notes, desktops, () => { }, () => { },
                () => { }, () => { }, () => { }, () => { });
            try
            {
                window.Navigate(SettingsSection.DesktopMemory);
                var memory = (DesktopMemorySettingsPage)window.MemoryHost.Content;
                window.Navigate(SettingsSection.General);
                window.Navigate(SettingsSection.DesktopMemory);
                Assert.AreSame(memory, window.MemoryHost.Content);
                window.Navigate(SettingsSection.GitWatcher);
                window.Navigate(SettingsSection.VirtualDesktops);
                window.Navigate(SettingsSection.StickyNotes);
                var items = memory.DesktopPicker.ItemsSource;
                window.Close();

                Assert.IsNull(window.MemoryHost.Content);
                Assert.IsNull(window.GitOverlayHost.Content);
                Assert.IsNull(window.DesktopOverlayHost.Content);
                Assert.IsNull(window.NotesHost.Content);
                Assert.AreEqual(0, window.DesktopColorRows.Children.Count);
                memory.RefreshDesktops();
                Assert.AreSame(items, memory.DesktopPicker.ItemsSource);
                window.Navigate(SettingsSection.DesktopMemory);
                window.RefreshExternalSettings();
                Assert.IsNull(window.MemoryHost.Content);
            }
            finally { if (window.IsLoaded) window.Close(); }
        });
    }
}
