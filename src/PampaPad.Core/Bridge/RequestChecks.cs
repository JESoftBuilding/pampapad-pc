namespace PampaPad.Core;

/// <summary>
/// La validación de entrada de los pedidos que escriben (plan de la etapa 8, E8-7): corre en Bridge.Register antes del manejador, con el
/// pedido ya deserializado. El deserializador deja pasar un campo que falta o un null dentro de una lista, y llegaban al manejador: un
/// null en actions reventaba con internal_error o seguía hasta la huella y el disco. Cada uno contesta invalid_data con dónde, en la
/// ruta de los datos del pedido (config.actions[0]).
/// </summary>
public static class RequestChecks
{
    public static Failure? ConfigSave(ConfigSaveRequest request) =>
        Missing(request.Config, "config") ?? Missing(request.ExpectedFingerprint, "expectedFingerprint") ?? Config(request.Config!, "config");

    public static Failure? ActionTest(ActionTestRequest request) => Missing(request.Action, "action") ?? Action(request.Action, "action");

    public static Failure? ScriptsWrite(ScriptsWriteRequest request) =>
        Missing(request.Id, "id") ?? Missing(request.Manifest, "manifest") ?? Missing(request.ExpectedFingerprint, "expectedFingerprint")
        ?? NullIn(request.Manifest!.Tags, "manifest.tags");

    public static Failure? ScriptsTest(ScriptsTestRequest request) => Missing(request.Id, "id");

    public static Failure? ActionRun(ActionRunRequest request) => Missing(request.Id, "id");

    public static Failure? ActionSave(ActionSaveRequest request) =>
        Missing(request.Action, "action") ?? Missing(request.Action!.Id, "action.id") ?? Missing(request.ExpectedFingerprint, "expectedFingerprint") ?? Action(request.Action, "action");

    public static Failure? KeyAssign(KeyAssignRequest request) =>
        Missing(request.Page, "page") ?? Missing(request.Position, "position") ?? Missing(request.ExpectedFingerprint, "expectedFingerprint")
        ?? (request.Position is < 1 or > Validator.KeyCount ? new Failure(ErrorCodes.InvalidData, $"position va de 1 a {Validator.KeyCount}: no hay una tecla {request.Position}.", "position") : null)
        ?? NullIn(request.ShortLabel, "shortLabel");

    public static Failure? SettingsSave(SettingsSaveRequest request) =>
        request is { StartWithWindows: null, SkippedVersion: null, McpEnabled: null }
            ? new Failure(ErrorCodes.InvalidData, "settings.save no trae ningún ajuste: van startWithWindows, skippedVersion o mcpEnabled.", "data")
            : null;

    private static Failure? Config(Config config, string at) =>
        NullIn(config.Pages, at + ".pages", (page, here) => NullIn(page.Keys, here + ".keys", (key, inKey) => NullIn(key.ShortLabel, inKey + ".shortLabel")))
        ?? NullIn(config.Actions, at + ".actions", Action)
        ?? NullIn(config.Hotkeys, at + ".hotkeys")
        ?? NullIn(config.Preflight, at + ".preflight");

    private static Failure? Action(Action? action, string at) => action is null ? null : NullIn(action.Steps, at + ".steps");

    private static Failure? Missing(object? value, string where) =>
        value is null ? new Failure(ErrorCodes.InvalidData, $"Falta {where}.", where) : null;

    private static Failure? NullIn<T>(IReadOnlyList<T>? list, string at, Func<T, string, Failure?>? inside = null)
    {
        for (int i = 0; i < (list?.Count ?? 0); i++)
        {
            string here = $"{at}[{i}]";
            if (list![i] is not { } item)
            {
                return new Failure(ErrorCodes.InvalidData, $"{here} es null: una lista no lleva elementos vacíos.", here);
            }

            if (inside?.Invoke(item, here) is { } failed)
            {
                return failed;
            }
        }

        return null;
    }
}
