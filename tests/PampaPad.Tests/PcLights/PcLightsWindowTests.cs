using System.Text.Json;
using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La tarjeta "Luces de la PC" por el puente (plan de la 1.6.0, RGB-4): las zonas por options.list y la zona elegida por settings.save,
/// con el doble de OpenRGB y el reloj falso. Ninguna prueba mueve el reloj para que el cambio valga: guardar despierta al trabajador.
/// </summary>
public sealed class PcLightsWindowTests : IAsyncLifetime
{
    private readonly DispatcherRig _dispatcher = new(DispatcherRig.Example);
    private PcLightsRig? _lights;
    private Bridge? _bridge;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_lights is not null)
        {
            await _lights.DisposeAsync();
        }

        await _dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task The_zones_come_as_options_with_the_device_as_detail_even_without_a_zone_chosen()
    {
        Start(null);

        using JsonDocument reply = await AskAsync("options.list", """{"list":"pcLightZones"}""");

        JsonElement data = Data(reply);
        Assert.Equal(
            ["Corsair · Fans", $"{FakeOpenRgb.Board} · Back I/O", $"{FakeOpenRgb.Board} · {FakeOpenRgb.Header}"],
            data.GetProperty("items").EnumerateArray().Select(item => $"{item.GetProperty("detail").GetString()} · {item.GetProperty("name").GetString()}"));
        Assert.False(data.TryGetProperty("unavailable", out _));
        Assert.Equal(PcLightsLink.Off, _lights!.State.Current.PcLights);
    }

    [Fact]
    public async Task Without_openrgb_the_list_is_empty_and_says_why_in_a_few_words()
    {
        Start(null, failConnects: 1);

        using JsonDocument reply = await AskAsync("options.list", """{"list":"pcLightZones"}""");

        JsonElement data = Data(reply);
        Assert.Equal(0, data.GetProperty("items").GetArrayLength());
        Assert.Equal(ActionsRequests.PcLightZonesUnavailable, data.GetProperty("unavailable").GetString());
    }

    // Con la zona elegida y OpenRGB caído, el trabajador espera 5 s para reintentar: pedir las zonas no espera ese tiempo, prueba una vez
    // y el reintento sigue siendo uno solo (decisión 6).
    [Fact]
    public async Task With_openrgb_down_the_zones_answer_without_waiting_the_retry_and_the_retry_stays_single()
    {
        Start(PcLightsRig.Chosen, failConnects: 2);
        await _lights!.UntilAsync(() => _lights.Link == PcLightsLinkState.Unreachable, "sin OpenRGB");

        using JsonDocument reply = await AskAsync("options.list", """{"list":"pcLightZones"}""");

        Assert.Equal(ActionsRequests.PcLightZonesUnavailable, Data(reply).GetProperty("unavailable").GetString());
        Assert.Equal(2, _lights.OpenRgb.Connects);
        await Eventually.AdvancingAsync(_lights.Clock, PcLights.RetryEvery, () => _lights.Link == PcLightsLinkState.Connected, "volvió");
        Assert.Equal(3, _lights.OpenRgb.Connects);
    }

    [Fact]
    public async Task Choosing_a_zone_keeps_last_and_what_the_program_does_not_know()
    {
        Start($$"""
            {
              "version": 1,
              "futuro": { "a": 1 },
              "pcLights": { "device": "Otra", "zone": "Vieja", "otro": true, "last": { "on": true, "color": { "r": 1, "g": 2, "b": 3 }, "brightness": 9 } }
            }
            """);

        using JsonDocument reply = await AskAsync("settings.save", $$$"""{"pcLights":{"device":"{{{FakeOpenRgb.Board}}}","zone":"{{{FakeOpenRgb.Header}}}"}}""");

        Assert.Equal(FakeOpenRgb.Header, Data(reply).GetProperty("pcLights").GetProperty("zone").GetString());
        JsonNode file = JsonNode.Parse(File.ReadAllText(_lights!.Path))!;
        Assert.Equal(FakeOpenRgb.Board, (string?)file["pcLights"]!["device"]);
        Assert.Equal(FakeOpenRgb.Header, (string?)file["pcLights"]!["zone"]);
        Assert.True((bool?)file["pcLights"]!["otro"]);
        Assert.Equal(9, (int?)file["pcLights"]!["last"]!["brightness"]);
        Assert.Equal(1, (int?)file["futuro"]!["a"]);
    }

    // Guardar otro ajuste de Ajustes no toca pcLights: la tarjeta sin OpenRGB conserva la zona elegida (revisión de RGB-4).
    [Fact]
    public async Task Saving_another_setting_keeps_pcLights_as_it_was()
    {
        Start($$"""{ "version": 1, "pcLights": { "device": "{{FakeOpenRgb.Board}}", "zone": "{{FakeOpenRgb.Header}}", "last": { "on": true, "color": { "r": 1, "g": 2, "b": 3 }, "brightness": 9 } } }""");

        using JsonDocument reply = await AskAsync("settings.save", """{"consolePort":"COM7"}""");

        Assert.Equal(FakeOpenRgb.Header, Data(reply).GetProperty("pcLights").GetProperty("zone").GetString());
        JsonNode file = JsonNode.Parse(File.ReadAllText(_lights!.Path))!;
        Assert.Equal("COM7", (string?)file["consolePort"]);
        Assert.Equal(FakeOpenRgb.Board, (string?)file["pcLights"]!["device"]);
        Assert.Equal(FakeOpenRgb.Header, (string?)file["pcLights"]!["zone"]);
        Assert.Equal(9, (int?)file["pcLights"]!["last"]!["brightness"]);
    }

    [Fact]
    public async Task Do_not_handle_takes_the_whole_pcLights_out_of_local_json()
    {
        Start("""{ "version": 1, "consolePort": "COM7", "pcLights": { "device": "Otra", "zone": "Vieja", "otro": true } }""");

        using JsonDocument reply = await AskAsync("settings.save", """{"pcLights":{"device":"","zone":""}}""");

        Assert.False(Data(reply).TryGetProperty("pcLights", out _));
        JsonNode file = JsonNode.Parse(File.ReadAllText(_lights!.Path))!;
        Assert.Null(file["pcLights"]);
        Assert.Equal("COM7", (string?)file["consolePort"]);
    }

    [Theory]
    [InlineData("""{"device":"Placa","zone":""}""")]
    [InlineData("""{"zone":"RGB Header"}""")]
    public async Task A_device_without_a_zone_or_a_zone_without_a_device_is_refused(string pcLights)
    {
        Start(null);

        using JsonDocument reply = await AskAsync("settings.save", $$$"""{"pcLights":{{{pcLights}}}}""");

        Assert.Equal(ErrorCodes.InvalidData, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("pcLights", reply.RootElement.GetProperty("error").GetProperty("where").GetString());
        Assert.False(File.Exists(_lights!.Path));
    }

    [Fact]
    public async Task Saving_wakes_the_worker_so_the_state_changes_without_a_command()
    {
        Start(null);
        await _lights!.UntilAsync(() => _lights.Link == PcLightsLinkState.Off, "apagado");

        using JsonDocument chosen = await AskAsync("settings.save", $$$"""{"pcLights":{"device":"{{{FakeOpenRgb.Board}}}","zone":"{{{FakeOpenRgb.Header}}}"}}""");
        await _lights.UntilAsync(() => _lights.Link == PcLightsLinkState.Connected, "conectado");
        Assert.Equal($"{FakeOpenRgb.Board} · {FakeOpenRgb.Header}", _lights.State.Current.PcLights.Zone);

        using JsonDocument none = await AskAsync("settings.save", """{"pcLights":{"device":"","zone":""}}""");
        await _lights.UntilAsync(() => _lights.Link == PcLightsLinkState.Off, "apagado otra vez");
        Assert.Empty(_lights.OpenRgb.Writes);
    }

    // Sin OpenRGB el trabajador está en la espera de 5 s: "No manejar" no la espera, y sin zona no queda nada por reintentar.
    [Fact]
    public async Task Do_not_handle_while_openrgb_is_down_is_off_at_once_and_stops_retrying()
    {
        Start(PcLightsRig.Chosen, failConnects: 100);
        await _lights!.UntilAsync(() => _lights.Link == PcLightsLinkState.Unreachable, "sin OpenRGB");

        using JsonDocument reply = await AskAsync("settings.save", """{"pcLights":{"device":"","zone":""}}""");
        await _lights.UntilAsync(() => _lights.Link == PcLightsLinkState.Off, "apagado");

        int connects = _lights.OpenRgb.Connects;
        await Eventually.StepsAsync(_lights.Clock, PcLights.RetryEvery, 2);
        Assert.Equal(connects, _lights.OpenRgb.Connects);
    }

    private void Start(string? localJson, int failConnects = 0)
    {
        _lights = new PcLightsRig(localJson, failConnects).Start();
        PcLightsRig lights = _lights;
        DispatcherRig rig = _dispatcher;
        _bridge = new Bridge(rig.State, rig.Log);
        ActionsRequests.Register(_bridge, new EditSources(rig.Library, rig.State, rig.Log, rig.Executor, [rig.Marker], [], rig.Dispatcher, new FakeWindows(), null, PcLights: lights.Lights));
        var startup = new FakeStartup(null);
        SettingsRequests.Register(_bridge, new SettingsSources(
            lights.Settings, startup, null, null, () => new SettingsView(false, null, null, PcLights: PcLightsChoice.Of(lights.Settings.Current.PcLights)), PcLightsChanged: lights.Lights.SettingsChanged));
    }

    // Con tope de tiempo real: un pedido que se queda esperando el reloj falso hace fallar la prueba en vez de colgar la corrida (con el
    // corte desactivado, el pedido de zonas esperaba para siempre el reintento de 5 s; revisión de RGB-4).
    private async Task<JsonDocument> AskAsync(string request, string data) =>
        JsonDocument.Parse(await _bridge!.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Window, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15)));

    private static JsonElement Data(JsonDocument reply)
    {
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.GetRawText());
        return reply.RootElement.GetProperty("data").Clone();
    }
}
