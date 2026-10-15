using PampaPad.Core;

namespace PampaPad.Tests;

public class BridgePolicyTests
{
    private static readonly PolicyContext Idle = new(McpAllowed: true, TakeInProgress: false);
    private static readonly PolicyContext Take = new(McpAllowed: true, TakeInProgress: true);
    private static readonly PolicyContext Off = new(McpAllowed: false, TakeInProgress: false);

    // Pedido, clase, puerta, quién llama, momento y el código que tiene que salir (null: pasa). Las filas siguen la tabla de 01 §5.
    public static TheoryData<string, RequestKind, string, Caller, string, string?> Rules() => new()
    {
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Window, "idle", null },
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Tray, "idle", ErrorCodes.NoPermission },
        { "app.snapshot", RequestKind.Read, "window", Caller.Internal, "idle", null },
        { "settings.save", RequestKind.WindowOnly, "window,mcp,tray", Caller.Mcp, "idle", ErrorCodes.NoPermission },
        { "settings.save", RequestKind.WindowOnly, "window,tray", Caller.Tray, "take", null },
        { "config.save", RequestKind.Write, "window", Caller.Mcp, "idle", ErrorCodes.NoPermission },
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Mcp, "off", ErrorCodes.NoPermission },
        // El interruptor es sólo del MCP: con el permiso apagado, la ventana, la bandeja y el programa siguen pasando (01 §5).
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Window, "off", null },
        { "settings.save", RequestKind.WindowOnly, "window,tray", Caller.Tray, "off", null },
        { "app.snapshot", RequestKind.Read, "window", Caller.Internal, "off", null },
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Mcp, "idle", null },
        { "app.snapshot", RequestKind.Read, "window,mcp", Caller.Mcp, "take", null },
        { "scripts.write", RequestKind.Write, "window,mcp", Caller.Mcp, "take", ErrorCodes.ReadOnlyWhileRecording },
        { "action.run", RequestKind.Execute, "mcp", Caller.Mcp, "take", ErrorCodes.ReadOnlyWhileRecording },
        { "action.run", RequestKind.Execute, "mcp", Caller.Mcp, "idle", null },
        { "action.cancel", RequestKind.Execute, "window,tray", Caller.Window, "take", null },
        { "action.test", RequestKind.Execute, "window", Caller.Window, "take", ErrorCodes.ReadOnlyWhileRecording },
        { "action.test", RequestKind.Execute, "window", Caller.Window, "idle", null },
        { "scripts.test", RequestKind.Execute, "window,mcp", Caller.Internal, "take", ErrorCodes.ReadOnlyWhileRecording },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Policy_rules(string request, RequestKind kind, string door, Caller caller, string moment, string? expected)
    {
        Failure? failure = BridgePolicy.Check(request, kind, Door(door), caller, Moment(moment));

        Assert.Equal(expected, failure?.Code);
    }

    [Fact]
    public void With_the_mcp_switched_off_the_reason_says_so()
    {
        Failure? failure = BridgePolicy.Check("app.snapshot", RequestKind.Read, Doors.WindowAndMcp, Caller.Mcp, Off);

        Assert.Equal("El MCP está apagado en Ajustes", failure?.Message);
    }

    [Fact]
    public void A_refusal_says_which_request_and_from_where()
    {
        Failure? failure = BridgePolicy.Check("app.snapshot", RequestKind.Read, Doors.WindowAndMcp, Caller.Tray, Idle);

        Assert.Equal("app.snapshot no se puede pedir desde la bandeja.", failure?.Message);
    }

    [Theory]
    [InlineData(RecordingState.Stopped, false, false)]
    [InlineData(RecordingState.Starting, false, true)]
    [InlineData(RecordingState.Recording, false, true)]
    [InlineData(RecordingState.Paused, false, true)]
    [InlineData(RecordingState.Stopping, false, true)]
    [InlineData(RecordingState.Stopped, true, true)]
    public void A_take_is_recording_paused_or_streaming(RecordingState recording, bool streaming, bool take)
    {
        AppState state = AppState.Initial with { Obs = ObsState.Initial with { Recording = recording, Streaming = streaming } };

        Assert.Equal(take, PolicyContext.From(state, mcpAllowed: true).TakeInProgress);
    }

    [Theory]
    [InlineData(Caller.Window, Trigger.Window)]
    [InlineData(Caller.Mcp, Trigger.Mcp)]
    [InlineData(Caller.Tray, Trigger.Tray)]
    [InlineData(Caller.Internal, Trigger.Internal)]
    public void The_caller_goes_to_the_log_as_its_trigger(Caller caller, Trigger trigger)
    {
        Assert.Equal(trigger, BridgePolicy.AsTrigger(caller));
    }

    [Fact]
    public void The_doors_are_the_ones_of_05_section_11()
    {
        Assert.True(Doors.Window.SetEquals([Caller.Window]));
        Assert.True(Doors.WindowAndMcp.SetEquals([Caller.Window, Caller.Mcp]));
        Assert.True(Doors.WindowAndTray.SetEquals([Caller.Window, Caller.Tray]));
    }

    private static IReadOnlySet<Caller> Door(string list) =>
        list.Split(',').Select(name => Enum.Parse<Caller>(name, ignoreCase: true)).ToHashSet();

    private static PolicyContext Moment(string moment) => moment switch
    {
        "idle" => Idle,
        "take" => Take,
        "off" => Off,
        _ => throw new ArgumentOutOfRangeException(nameof(moment), moment, "Momento desconocido"),
    };
}
