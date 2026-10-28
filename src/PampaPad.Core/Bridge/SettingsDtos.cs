namespace PampaPad.Core;

/// <summary>El arranque con Windows como lo muestra la tarjeta Programa: no disponible en una copia sin instalar.</summary>
public enum StartupStatus
{
    Unavailable,
    On,
    Off,
}

/// <summary>
/// Cómo se conecta con OBS (03 §2): la dirección, 127.0.0.1 con el puerto del archivo de OBS, y de dónde sale la clave. La clave nunca
/// viaja: el programa la lee del archivo de OBS en cada intento.
/// </summary>
public sealed record ObsSettingsView(string Address, string Password = ObsSettingsView.SavedInObs)
{
    public const string SavedInObs = "la guardada en OBS";
}

/// <summary>
/// Los ajustes de esta PC que la página muestra. HotkeysEnabled dice si esta copia tomó los atajos (05 §10). StartWithWindows sale de
/// Windows al armar la foto, no de local.json (02 §2). Los dos del final, con su valor por defecto, entran en la etapa 8.
/// </summary>
public sealed record SettingsView(
    bool HotkeysEnabled, string? ConsolePort, string? SkippedVersion, StartupStatus StartWithWindows = StartupStatus.Unavailable, ObsSettingsView? Obs = null)
{
    public static StartupStatus Startup(bool? enabled) => enabled switch
    {
        true => StartupStatus.On,
        false => StartupStatus.Off,
        null => StartupStatus.Unavailable,
    };
}

/// <summary>settings.save: sólo los campos que cambiaron. skippedVersion "" borra la versión salteada. Contesta el SettingsView nuevo.</summary>
public sealed record SettingsSaveRequest(bool? StartWithWindows = null, string? SkippedVersion = null);
