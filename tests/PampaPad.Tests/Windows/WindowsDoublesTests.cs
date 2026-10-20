using System.Diagnostics;
using PampaPad.Core;

namespace PampaPad.Tests;

public class WindowsDoublesTests
{
    private static readonly WindowInfo Mirror = new("bancocam", "scrcpy", 1823, 850, Minimized: false);

    [Fact]
    public void A_window_can_appear_at_the_third_list()
    {
        var windows = new FakeWindows();
        windows.OpenFrom(3, Mirror);

        Assert.Empty(windows.List());
        Assert.Empty(windows.List());
        Assert.Equal([Mirror], windows.List());
        Assert.Equal([Mirror], windows.List());
        Assert.Equal(4, windows.Lists);
    }

    [Fact]
    public void A_window_can_appear_when_something_happened_and_close_later()
    {
        var windows = new FakeWindows();
        bool launched = false;
        windows.OpenWhen(() => launched, Mirror);

        Assert.Empty(windows.List());
        launched = true;
        Assert.Equal([Mirror], windows.List());
        windows.Close("bancocam");
        Assert.Empty(windows.List());
    }

    [Fact]
    public void The_real_list_does_not_fail_and_finds_this_process_running()
    {
        var windows = new Win32Windows();
        using Process self = Process.GetCurrentProcess();

        IReadOnlyList<WindowInfo> listed = windows.List();

        Assert.All(listed, window => Assert.False(string.IsNullOrEmpty(window.Title)));
        Assert.True(windows.IsProcessRunning(self.ProcessName));
        Assert.False(windows.IsProcessRunning("pampapad-proceso-que-no-existe"));
    }
}
