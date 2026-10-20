using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

public class FakeObsScenesTests
{
    [Fact]
    public async Task The_inventory_is_the_one_of_the_backup_of_october_15()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var scenes = await raw.AskAsync("1", "GetSceneList");

        Assert.Equal(16, scenes["responseData"]!["scenes"]!.AsArray().Count);
        Assert.Equal("Pantalla", (string)scenes["responseData"]!["currentProgramSceneName"]!);
        Assert.Contains("Vertical SolidWorks", fake.SceneNames);
    }

    [Fact]
    public async Task Changing_the_program_scene_answers_100_and_then_says_which_one_is_on_air()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var reply = await raw.AskAsync("1", "SetCurrentProgramScene", new { sceneUuid = FakeObs.CameraUuid });
        var changed = await raw.ReadAsync();

        Assert.Equal(100, (int)reply["requestStatus"]!["code"]!);
        Assert.Equal("CurrentProgramSceneChanged", (string)changed!["d"]!["eventType"]!);
        Assert.Equal(FakeObs.CameraUuid, (string)changed["d"]!["eventData"]!["sceneUuid"]!);
        Assert.Equal("Cámara", fake.SceneName);
    }

    [Fact]
    public async Task The_scene_already_on_air_answers_100_and_sends_no_event()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var reply = await raw.AskAsync("1", "SetCurrentProgramScene", new { sceneName = "Pantalla" });
        await fake.SetMicMutedAsync(true);
        var next = await raw.ReadAsync();

        Assert.Equal(100, (int)reply["requestStatus"]!["code"]!);
        Assert.Equal("InputMuteStateChanged", (string)next!["d"]!["eventType"]!);      // (Medido: E0-1) no hubo evento de escena
    }

    [Fact]
    public async Task A_scene_that_does_not_exist_is_600()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var reply = await raw.AskAsync("1", "SetCurrentProgramScene", new { sceneUuid = "00000000-0000-0000-0000-000000000000" });

        Assert.Equal(600, (int)reply["requestStatus"]!["code"]!);
        Assert.Equal("Pantalla", fake.SceneName);
    }

    [Fact]
    public async Task Muting_an_input_answers_100_and_then_the_event()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var reply = await raw.AskAsync("1", "SetInputMute", new { inputUuid = FakeObs.StingUuid, inputMuted = true });
        var muted = await raw.ReadAsync();
        var read = await raw.AskAsync("2", "GetInputMute", new { inputUuid = FakeObs.StingUuid });

        Assert.Equal(100, (int)reply["requestStatus"]!["code"]!);
        Assert.Equal("InputMuteStateChanged", (string)muted!["d"]!["eventType"]!);
        Assert.Equal("Sting triste", (string)muted["d"]!["eventData"]!["inputName"]!);
        Assert.True((bool)read["responseData"]!["inputMuted"]!);
        Assert.True(fake.IsMuted(FakeObs.StingUuid));
        Assert.False(fake.MicMuted);
    }

    [Fact]
    public async Task An_input_without_audio_is_604_to_read_it_and_to_mute_it()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var read = await raw.AskAsync("1", "GetInputMute", new { inputUuid = FakeObs.ScreenCaptureUuid });
        var mute = await raw.AskAsync("2", "SetInputMute", new { inputUuid = FakeObs.ScreenCaptureUuid, inputMuted = true });

        Assert.Equal(604, (int)read["requestStatus"]!["code"]!);
        Assert.Equal(604, (int)mute["requestStatus"]!["code"]!);
        Assert.Equal("The specified input does not support audio.", (string)mute["requestStatus"]!["comment"]!);
    }

    [Fact]
    public async Task What_is_done_in_the_window_of_obs_arrives_as_events_and_changes_the_lists()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        await fake.RenameSceneAsync(FakeObs.BancoUuid, "Banco de trabajo");
        var renamed = await raw.ReadAsync();
        await fake.RemoveSceneAsync(FakeObs.BancoUuid);
        var removed = await raw.ReadAsync();
        await fake.RemoveInputAsync(FakeObs.StingUuid);
        var gone = await raw.ReadAsync();
        var inputs = await raw.AskAsync("1", "GetInputList");

        Assert.Equal("SceneNameChanged", (string)renamed!["d"]!["eventType"]!);
        Assert.Equal("Banco", (string)renamed["d"]!["eventData"]!["oldSceneName"]!);
        Assert.Equal("SceneRemoved", (string)removed!["d"]!["eventType"]!);
        Assert.Equal("InputRemoved", (string)gone!["d"]!["eventType"]!);
        Assert.Equal(15, fake.SceneNames.Count);
        Assert.Equal(5, inputs["responseData"]!["inputs"]!.AsArray().Count);         // seis desde la etapa 5, menos la que se borró
    }

    [Fact]
    public async Task The_stream_settings_bring_an_invented_key()
    {
        await using var fake = FakeObs.Start();
        await using var raw = await RawObs.ConnectAsync(fake.Address);
        await raw.IdentifyAsync();

        var settings = await raw.AskAsync("1", "GetStreamServiceSettings");

        Assert.Equal(FakeObs.StreamKey, (string)settings["responseData"]!["streamServiceSettings"]!["key"]!);
    }
}
