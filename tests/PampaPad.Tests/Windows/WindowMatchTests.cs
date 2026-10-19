using PampaPad.Core;

namespace PampaPad.Tests;

public class WindowMatchTests
{
    private static readonly WindowInfo Mirror = new("bancocam", "scrcpy", 1823, 850, Minimized: false);
    private static readonly WindowInfo Folder = new("bancocam", "explorer", 960, 600, Minimized: false);

    [Fact]
    public void The_title_is_compared_whole_and_without_case_like_bancocam_ps1()
    {
        IReadOnlyList<WindowInfo> windows = [Mirror, new("bancocam - copia", "notepad", 800, 600, false)];

        Assert.Equal([Mirror], WindowMatch.Find(windows, "BancoCam", null));
        Assert.Empty(WindowMatch.Find(windows, "banco", null));
    }

    [Theory]
    [InlineData("scrcpy")]
    [InlineData("scrcpy.exe")]
    [InlineData("SCRCPY")]
    public void With_a_process_only_its_windows_count(string process)
    {
        Assert.Equal([Mirror], WindowMatch.Find([Folder, Mirror], "bancocam", process));
    }

    [Fact]
    public void Without_a_process_a_folder_with_the_same_name_counts_too()
    {
        Assert.Equal([Folder, Mirror], WindowMatch.Find([Folder, Mirror], "bancocam", null));
    }

    [Fact]
    public void The_size_is_exact_and_a_minimized_window_has_it_only_when_no_size_is_asked()
    {
        Assert.True(WindowMatch.HasSize(Mirror, 1823, 850));
        Assert.True(WindowMatch.HasSize(Mirror, null, null));
        Assert.False(WindowMatch.HasSize(Mirror with { Width = 1824 }, 1823, 850));
        Assert.False(WindowMatch.HasSize(Mirror with { Height = 851 }, 1823, 850));
        Assert.False(WindowMatch.HasSize(Mirror with { Minimized = true }, 1823, 850));
        Assert.False(WindowMatch.HasSize(Mirror with { Minimized = true }, 1823, null));

        // wait.window pide el ancho y el alto por separado (02 §5): con uno solo, se compara ése y el otro no cuenta.
        Assert.True(WindowMatch.HasSize(Mirror with { Height = 851 }, 1823, null));
        Assert.False(WindowMatch.HasSize(Mirror with { Height = 851 }, null, 850));
        Assert.True(WindowMatch.HasSize(Mirror with { Width = 1824 }, null, 850));

        // 02 §5: wait.window exige el tamaño sólo "si se piden"; sin ancho ni alto, una minimizada existe igual, como para window.exists.
        Assert.True(WindowMatch.HasSize(Mirror with { Minimized = true, Width = 160, Height = 28 }, null, null));
    }

    [Fact]
    public void The_detail_says_what_was_measured_and_what_was_expected()
    {
        Assert.Equal("No hay una ventana \"bancocam\" de scrcpy.", WindowMatch.Describe([], "bancocam", "scrcpy.exe", 1823, 850));
        Assert.Equal("La ventana \"bancocam\" mide 1823 x 850.", WindowMatch.Describe([Mirror], "bancocam", null, 1823, 850));
        Assert.Equal(
            "La ventana \"bancocam\" mide 2279 x 1063 y se esperaba 1823 x 850.",
            WindowMatch.Describe([Mirror with { Width = 2279, Height = 1063 }], "bancocam", null, 1823, 850));
        Assert.Equal(
            "La ventana \"bancocam\" está minimizada y se esperaba 1823 x 850.",
            WindowMatch.Describe([Mirror with { Minimized = true }], "bancocam", null, 1823, 850));
        Assert.Equal(
            "La ventana \"bancocam\" está minimizada.",
            WindowMatch.Describe([Mirror with { Minimized = true, Width = 160, Height = 28 }], "bancocam", null, null, null));
        Assert.Equal(
            "La ventana \"bancocam\" mide 960 x 600 y se esperaba 1823 x cualquier alto.",
            WindowMatch.Describe([Folder], "bancocam", null, 1823, null));
        Assert.Equal(
            "La ventana \"bancocam\" mide 960 x 600 y se esperaba cualquier ancho x 850.",
            WindowMatch.Describe([Folder], "bancocam", null, null, 850));
    }

    [Fact]
    public void Among_several_with_the_same_title_the_one_with_the_size_wins()
    {
        string detail = WindowMatch.Describe([Folder, Mirror], "bancocam", null, 1823, 850);

        // Sin tamaño pedido sirven las dos, y se dice la que se ve antes que la minimizada.
        string shown = WindowMatch.Describe([Mirror with { Minimized = true, Width = 160, Height = 28 }, Mirror], "bancocam", null, null, null);

        Assert.Equal("La ventana \"bancocam\" mide 1823 x 850.", detail);
        Assert.Equal("La ventana \"bancocam\" mide 1823 x 850.", shown);
    }
}
