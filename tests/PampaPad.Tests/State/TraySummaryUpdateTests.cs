using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La actualización en la bandeja: el texto de ayuda y los dos ítems del menú, los dos calculados de AppState (05 §3 y §8).
/// El motivo por el que no se puede instalar sale de RestartPolicy cada vez que se pinta: no queda guardado en el estado.
/// </summary>
public class TraySummaryUpdateTests
{
    private static readonly ObsState Connected = ObsState.Initial with { Link = ObsLinkState.Ready };
    private static readonly ObsState Recording = Connected with { Recording = RecordingState.Recording, Recorded = TimeSpan.FromSeconds(5) };

    private static AppState With(UpdateState update, ObsState? obs = null) => AppState.Initial with { Obs = obs ?? Connected, Update = update };

    private static UpdateMenu Items(UpdateState update, ObsState? obs = null) => TraySummary.UpdateItems(With(update, obs));

    [Theory]
    [InlineData(UpdatePhase.Available, "0.1.1", null, "Versión 0.1.1 lista para instalar")]
    [InlineData(UpdatePhase.Available, "0.1.1", "No se pudo bajar: Se cortó", "Versión 0.1.1 lista para instalar. No se pudo bajar: Se cortó")]
    [InlineData(UpdatePhase.Downloading, "0.1.1", null, "Bajando la versión 0.1.1")]
    [InlineData(UpdatePhase.Applying, "0.1.1", null, "Instalando la versión 0.1.1")]
    [InlineData(UpdatePhase.Idle, "0.1.1", null, null)]
    [InlineData(UpdatePhase.Idle, null, null, null)]
    [InlineData(UpdatePhase.Available, null, null, null)]
    [InlineData(UpdatePhase.Downloading, null, null, null)]
    [InlineData(UpdatePhase.Applying, null, null, null)]
    public void The_update_text_follows_the_phase(UpdatePhase phase, string? version, string? detail, string? expected)
    {
        Assert.Equal(expected, TraySummary.UpdateText(With(new UpdateState(phase, version, detail))));
    }

    [Fact]
    public void Looking_again_while_an_update_is_offered_keeps_the_offer_in_the_tray()
    {
        // UpdateCycle.CheckAsync publica "before with { Phase = Checking }": la versión y la última falla siguen ahí hasta
        // que la búsqueda termine, y el ítem "Instalar" del menú sigue visible. La flecha no puede apagarse y prenderse.
        var checking = With(new UpdateState(UpdatePhase.Checking, "0.1.1"));
        var withFailure = With(new UpdateState(UpdatePhase.Checking, "0.1.1", "No se pudo bajar: Se cortó"));
        var first = With(new UpdateState(UpdatePhase.Checking));

        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(checking).Icon);
        Assert.Equal("Versión 0.1.1 lista para instalar", TraySummary.From(checking).Tooltip);
        Assert.Equal("Versión 0.1.1 lista para instalar. No se pudo bajar: Se cortó", TraySummary.From(withFailure).Tooltip);
        Assert.Equal(TrayIcon.Idle, TraySummary.From(first).Icon);
        Assert.Equal("PampaPad · OBS conectado", TraySummary.From(first).Tooltip);
    }

    [Fact]
    public void The_update_items_follow_the_phase()
    {
        // Lo que E1-4 pintaba en Tray.PaintUpdate sin prueba, con OBS conectado y sin grabar.
        Assert.Equal(new UpdateMenu(true, "Buscar actualización", false, false, "Instalar"), Items(UpdateState.Idle));
        Assert.Equal(new UpdateMenu(false, "Buscando actualización…", false, false, "Instalar"), Items(new UpdateState(UpdatePhase.Checking)));
        Assert.Equal(
            new UpdateMenu(false, "Buscando actualización…", true, false, "Instalar versión 0.1.1"),
            Items(new UpdateState(UpdatePhase.Checking, "0.1.1")));
        Assert.Equal(
            new UpdateMenu(true, "Buscar actualización", true, true, "Instalar versión 0.1.1"),
            Items(new UpdateState(UpdatePhase.Available, "0.1.1")));
        Assert.Equal(
            new UpdateMenu(true, "Buscar actualización", true, true, "Instalar versión 0.1.1"),
            Items(new UpdateState(UpdatePhase.Available, "0.1.1", "No se pudo bajar: Se cortó")));
        Assert.Equal(
            new UpdateMenu(false, "Buscar actualización", true, false, "Bajando la versión 0.1.1…"),
            Items(new UpdateState(UpdatePhase.Downloading, "0.1.1")));
        Assert.Equal(
            new UpdateMenu(false, "Buscar actualización", true, false, "Instalando la versión 0.1.1…"),
            Items(new UpdateState(UpdatePhase.Applying, "0.1.1")));
    }

    [Fact]
    public void While_recording_install_is_greyed_out_with_the_reason_next_to_it()
    {
        var recording = With(new UpdateState(UpdatePhase.Available, "0.2.0"), Recording);

        Assert.Equal(
            new UpdateMenu(true, "Buscar actualización", true, false, "Instalar versión 0.2.0 (Se está grabando)"),
            TraySummary.UpdateItems(recording));
        Assert.Equal(TrayIcon.Recording, TraySummary.From(recording).Icon);
        Assert.Equal("Grabando 0:00:05 · Versión 0.2.0 lista para instalar. Se está grabando", TraySummary.From(recording).Tooltip);
    }

    [Fact]
    public void When_the_take_ends_install_turns_on_and_the_reason_goes_away()
    {
        // El caso de la revisión de la etapa 2: con el motivo guardado en Detail, el texto seguía diciendo "Se está grabando".
        var recording = With(new UpdateState(UpdatePhase.Available, "0.2.0"), Recording);
        var stopped = recording with { Obs = Connected };

        Assert.False(TraySummary.UpdateItems(recording).InstallEnabled);
        Assert.Equal(new UpdateMenu(true, "Buscar actualización", true, true, "Instalar versión 0.2.0"), TraySummary.UpdateItems(stopped));
        Assert.Equal(TrayIcon.UpdateReady, TraySummary.From(stopped).Icon);
        Assert.Equal("Versión 0.2.0 lista para instalar", TraySummary.From(stopped).Tooltip);
    }

    [Fact]
    public void Every_reason_of_the_policy_greys_out_install()
    {
        var offer = new UpdateState(UpdatePhase.Available, "0.2.0");
        var paused = Items(offer, Connected with { Recording = RecordingState.Paused });
        var streaming = Items(offer, Connected with { Streaming = true });
        var starting = Items(offer, Connected with { Recording = RecordingState.Starting });
        var lost = Items(offer, Recording.WithLink(ObsLinkState.Lost));     // con el enlace cortado vale lo último que se supo

        Assert.Equal("Instalar versión 0.2.0 (La grabación está en pausa)", paused.InstallText);
        Assert.Equal("Instalar versión 0.2.0 (Se está transmitiendo)", streaming.InstallText);
        Assert.Equal("Instalar versión 0.2.0 (Se está grabando)", starting.InstallText);
        Assert.Equal("Instalar versión 0.2.0 (Se está grabando)", lost.InstallText);
        Assert.All(new[] { paused, streaming, starting, lost }, menu => Assert.False(menu.InstallEnabled));
    }

    [Fact]
    public void Looking_again_while_recording_keeps_the_reason_and_install_stays_off()
    {
        var checking = With(new UpdateState(UpdatePhase.Checking, "0.2.0"), Recording);

        Assert.Equal(
            new UpdateMenu(false, "Buscando actualización…", true, false, "Instalar versión 0.2.0 (Se está grabando)"),
            TraySummary.UpdateItems(checking));
        Assert.Equal("Grabando 0:00:05 · Versión 0.2.0 lista para instalar. Se está grabando", TraySummary.From(checking).Tooltip);
    }

    [Fact]
    public void A_failure_and_the_reason_both_show_while_the_policy_rules()
    {
        var both = With(new UpdateState(UpdatePhase.Available, "0.2.0", "No se pudo bajar: Se cortó"), Recording);

        Assert.Equal(
            "Grabando 0:00:05 · Versión 0.2.0 lista para instalar. No se pudo bajar: Se cortó. Se está grabando",
            TraySummary.From(both).Tooltip);
        Assert.Equal("Instalar versión 0.2.0 (Se está grabando)", TraySummary.UpdateItems(both).InstallText);
    }

    [Fact]
    public void A_take_that_starts_while_downloading_does_not_change_the_download_text()
    {
        // UpdateCycle vuelve a preguntar antes de aplicar; mientras baja, el ítem y el texto dicen sólo que baja.
        var downloading = With(new UpdateState(UpdatePhase.Downloading, "0.2.0"), Recording);

        Assert.Equal(
            new UpdateMenu(false, "Buscar actualización", true, false, "Bajando la versión 0.2.0…"),
            TraySummary.UpdateItems(downloading));
        Assert.Equal("Grabando 0:00:05 · Bajando la versión 0.2.0", TraySummary.From(downloading).Tooltip);
    }
}
