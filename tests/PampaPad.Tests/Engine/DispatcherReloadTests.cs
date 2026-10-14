using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La biblioteca cambia con el programa andando: recargar mientras corre una acción, desde varios hilos o con un archivo roto en el medio.</summary>
public class DispatcherReloadTests
{
    [Fact]
    public async Task Reloading_while_an_action_runs_leaves_it_running_and_the_next_press_uses_the_new_file()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        System.Action release = rig.Hold();
        rig.Windows.Press(DispatcherRig.Short);
        await Poll.Until(() => rig.State.Current.Running.Count == 1);

        rig.Write(DispatcherRig.Example.Replace("\"marker\": \"short\"", "\"marker\": \"fin\"", StringComparison.Ordinal));
        rig.Library.Load();

        Assert.Equal("marca-short", Assert.Single(rig.State.Current.Running).ActionId);
        release();
        await Poll.Until(() => rig.State.Current.Running.Count == 0);
        rig.Windows.Press(DispatcherRig.Short);
        await Poll.Until(() => rig.Finished.Count == 2);
        Assert.Equal(new[] { "short", "fin" }, rig.Marker.Runs.Select(step => step.Parameters["marker"].GetString()));
    }

    [Fact]
    public async Task Reloads_from_several_threads_end_with_the_hotkeys_of_the_file()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 15; i++)
            {
                rig.Library.Load();
            }
        })));
        rig.Library.Load();

        Assert.Equal(5, rig.Windows.Registered.Count);
        Assert.Empty(rig.State.Current.Problems);
        Assert.Equal(new HotkeysState(5, 0), rig.State.Current.Hotkeys);
        Assert.Equal(LibraryStatus.Loaded, rig.State.Current.Library.Status);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_broken_file_and_then_a_good_one_give_the_hotkeys_back()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        rig.Write("{ roto");
        rig.Library.Load();
        Assert.Empty(rig.Windows.Registered);
        rig.Write(DispatcherRig.Example);
        rig.Library.Load();

        Assert.Equal(5, rig.Windows.Registered.Count);
        Assert.Empty(rig.State.Current.Problems);
        Assert.Equal("grabacion", rig.State.Current.BasePage);
    }
}
