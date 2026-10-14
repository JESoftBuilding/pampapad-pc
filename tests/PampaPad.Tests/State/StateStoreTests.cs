using System.Collections.Concurrent;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class StateStoreTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Update_numbers_each_new_state()
    {
        await using var store = new StateStore();

        store.Update(state => Offering(state, "0.1.1"));
        store.Update(state => Offering(state, "0.1.2"));

        Assert.Equal(2L, store.Current.Seq);
        Assert.Equal("0.1.2", store.Current.Update.Version);
    }

    [Fact]
    public async Task A_change_that_changes_nothing_is_neither_numbered_nor_announced()
    {
        var seen = new ConcurrentQueue<long>();
        var store = new StateStore();
        using var subscription = store.Subscribe((_, current) => seen.Enqueue(current.Seq));

        store.Update(state => state);
        store.Update(state => Offering(state, "0.1.1"));
        store.Update(state => Offering(state, "0.1.1"));
        await store.DisposeAsync();

        Assert.Equal(1L, store.Current.Seq);
        Assert.Equal(new long[] { 1 }, seen);
    }

    [Fact]
    public async Task A_slow_subscriber_delays_notices_but_not_updates()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new ManualResetEventSlim(false);
        var seen = new ConcurrentQueue<long>();
        var store = new StateStore();
        using var subscription = store.Subscribe((_, current) =>
        {
            entered.TrySetResult();
            gate.Wait(Patience);
            seen.Enqueue(current.Seq);
        });

        store.Update(state => Offering(state, "0.1.1"));
        await entered.Task.WaitAsync(Patience);
        store.Update(state => Offering(state, "0.1.2"));
        store.Update(state => Offering(state, "0.1.3"));

        Assert.Equal(3L, store.Current.Seq);
        Assert.Empty(seen);

        gate.Set();
        await store.DisposeAsync();

        Assert.Equal(new long[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_others()
    {
        await using var log = new Log(_temp.Combine("log"), TimeProvider.System);
        var seen = new ConcurrentQueue<long>();
        var store = new StateStore(log);
        using var broken = store.Subscribe((_, _) => throw new InvalidOperationException("roto"));
        using var healthy = store.Subscribe((_, current) => seen.Enqueue(current.Seq));

        store.Update(state => Offering(state, "0.1.1"));
        store.Update(state => Offering(state, "0.1.2"));
        await store.DisposeAsync();

        Assert.Equal(new long[] { 1, 2 }, seen);
        Assert.Contains(log.Recent(), entry => entry.Module == "state" && entry.Code == ErrorCodes.InternalError);
    }

    [Fact]
    public async Task A_disposed_subscription_stops_receiving()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<long>();
        var store = new StateStore();
        var subscription = store.Subscribe((_, current) =>
        {
            seen.Enqueue(current.Seq);
            first.TrySetResult();
        });

        store.Update(state => Offering(state, "0.1.1"));
        await first.Task.WaitAsync(Patience);
        subscription.Dispose();
        store.Update(state => Offering(state, "0.1.2"));
        await store.DisposeAsync();

        Assert.Equal(new long[] { 1 }, seen);
    }

    private static AppState Offering(AppState state, string version) =>
        state with { Update = new UpdateState(UpdatePhase.Available, version) };
}
