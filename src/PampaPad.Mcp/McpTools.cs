using System.Reflection;
using ModelContextProtocol.Server;
using PampaPad.Mcp.Tools;

namespace PampaPad.Mcp;

/// <summary>
/// La lista fija de herramientas (05 §7): vive en el ejecutable, así responde aunque el programa esté cerrado. Cada una en su archivo de
/// Tools/; el nombre, la descripción y la marca de interacción salen de los atributos de su método.
/// </summary>
public static class McpTools
{
    /// <summary>Con esta marca en true, Claude Code pide aprobación en cada llamada, también en los modos que no preguntan (05 §7).</summary>
    public const string RequiresUserInteraction = "anthropic/requiresUserInteraction";

    /// <summary>Los nombres, en el orden de la lista: lo que imprime --smoke-test y lo que publish.ps1 compara.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Create(new PipeClient(string.Empty)).Select(tool => tool.ProtocolTool.Name)];

    public static IReadOnlyList<McpServerTool> Create(PipeClient pipe) =>
    [
        From(new StateRead(pipe)),
        From(new LibraryRead(pipe)),
        From(new ScriptsRead(pipe)),
        From(new LogRead(pipe)),
        From(new ActionSave(pipe)),
        From(new KeyAssign(pipe)),
        From(new ScriptWrite(pipe)),
        From(new ActionRun(pipe)),
        From(new ScriptTest(pipe)),
    ];

    private static McpServerTool From(object target) =>
        McpServerTool.Create(target.GetType().GetMethods().Single(method => method.IsDefined(typeof(McpServerToolAttribute))), target);
}
