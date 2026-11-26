using System.IO;
using System.Windows;

namespace BranchWatch.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OverlayWindowHostTests
{
    [TestMethod]
    public void DisabledRender_DoesNotConstructWindow()
    {
        StaTest.Run(() =>
        {
            var created = 0;
            using var host = new OverlayWindowHost<OverlayWindow>(() =>
            {
                created++;
                return new OverlayWindow();
            });
            host.Render(false, _ => Assert.Fail("Disabled overlay rendered."));
            host.Release();
            Assert.AreEqual(0, created);
            Assert.IsNull(host.Current);
        });
    }

    [TestMethod]
    public void GitOverlay_ReusesWhileEnabledAndRecreatesFromLatestState()
    {
        StaTest.Run(() =>
        {
            var settings = new AppSettings();
            var created = 0;
            using var host = new OverlayWindowHost<OverlayWindow>(() =>
            {
                created++;
                return new OverlayWindow();
            });

            void Render(string branch) => host.Render(true, window =>
            {
                window.SetOverlayText(branch, @"C:\repo");
                window.ApplySettings(settings);
            });

            Render("first");
            var first = host.Current!;
            var closed = 0;
            first.Closed += (_, _) => closed++;
            Render("second");
            Assert.AreSame(first, host.Current);
            Assert.AreEqual("second", first.BranchText.Text);
            Assert.AreEqual(1, created);

            host.Render(false, _ => Assert.Fail("Disabled overlay rendered."));
            Assert.AreEqual(1, closed);
            Assert.IsNull(host.Current);
            Render("latest");
            Assert.AreEqual(2, created);
            Assert.AreNotSame(first, host.Current);
            Assert.AreEqual("latest", host.Current!.BranchText.Text);

            host.Dispose();
            host.Render(true, _ => Assert.Fail("Disposed host rendered."));
            Assert.IsNull(host.Current);
            Assert.AreEqual(2, created);
        });
    }

    [TestMethod]
    public void ClosedWindow_IsNotRetainedOrReused()
    {
        StaTest.Run(() =>
        {
            using var host = new OverlayWindowHost<OverlayWindow>(() => new OverlayWindow());
            host.Render(true, _ => { });
            var first = host.Current!;
            first.Close();
            Assert.IsNull(host.Current);
            host.Render(true, _ => { });
            Assert.AreNotSame(first, host.Current);
        });
    }

    [TestMethod]
    public void FailedFirstRender_ReleasesCandidateAndAllowsRetry()
    {
        StaTest.Run(() =>
        {
            using var host = new OverlayWindowHost<OverlayWindow>(() => new OverlayWindow());
            var closed = 0;
            Assert.ThrowsExactly<InvalidOperationException>(() => host.Render(true, window =>
            {
                window.Closed += (_, _) => closed++;
                throw new InvalidOperationException();
            }));
            Assert.AreEqual(1, closed);
            Assert.IsNull(host.Current);
            host.Render(true, _ => { });
            Assert.IsNotNull(host.Current);
        });
    }

    [TestMethod]
    public void DesktopOverlay_DisabledStartupAndToggleReleaseTaskbarTimer()
    {
        StaTest.WithTemporaryDirectory(directory =>
        {
            var settings = new AppSettings
            {
                VirtualDesktopOverlayVisible = false,
                VirtualDesktopOverlayPositionPreset = "show-on-taskbar"
            };
            var service = new SettingsService(Path.Combine(directory, "settings.json"));
            using var controller = new VirtualDesktopOverlayController(service, settings);
            controller.RefreshSettings();
            Assert.IsNull(controller.CurrentOverlay);
            var monitor = controller.Monitor;

            Assert.IsTrue(controller.ToggleVisibility());
            var first = controller.CurrentOverlay!;
            Assert.IsTrue(first.IsVisible);
            Assert.IsTrue(first.IsTaskbarTimerRunning);
            controller.RefreshSettings();
            Assert.AreSame(first, controller.CurrentOverlay);

            Assert.IsFalse(controller.ToggleVisibility());
            Assert.IsNull(controller.CurrentOverlay);
            Assert.IsFalse(first.IsTaskbarTimerRunning);
            Assert.AreSame(monitor, controller.Monitor);

            Assert.IsTrue(controller.ToggleVisibility());
            var second = controller.CurrentOverlay!;
            Assert.AreNotSame(first, second);
            Assert.IsTrue(second.IsTaskbarTimerRunning);
            controller.Dispose();
            controller.RefreshSettings();
            Assert.IsNull(controller.CurrentOverlay);
            Assert.IsFalse(second.IsTaskbarTimerRunning);
        });
    }
}
