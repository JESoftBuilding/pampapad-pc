namespace PampaPad.Core;

/// <summary>log.read (05 §11): las últimas entradas que pasan los filtros, de la más vieja a la más nueva. Función pura.</summary>
public static class LogQuery
{
    /// <summary>Cuántas devuelve sin "count": las que la página muestra al abrir (Propuesto, sin fuente).</summary>
    public const int DefaultCount = 200;

    public static Result<LogReadResponse> Read(IReadOnlyList<LogEntry> recent, LogReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(request);

        int count = request.Count ?? DefaultCount;
        if (count is < 1 or > Log.MemoryLimit)
        {
            return Result<LogReadResponse>.Fail(
                ErrorCodes.InvalidData, $"count va de 1 a {Log.MemoryLimit}: es lo que el registro guarda en memoria (05 §9).", "count");
        }

        // Filtrar por acción y por script (05 §4.4) pide campos que LogEntry no tiene: entra cuando los tenga.
        IEnumerable<LogEntry> matching = recent
            .Where(entry => request.Level is not { } level || entry.Level >= level)
            .Where(entry => request.RequestedBy is not { } by || entry.RequestedBy == by)
            .Where(entry => request.RunId is not { } run || entry.RunId == run);
        return new LogReadResponse([.. matching.TakeLast(count)]);
    }
}
