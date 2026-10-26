namespace PampaPad.Core;

/// <summary>
/// Escribe un archivo sin dejarlo a medias (02 §3, regla 9): el temporal vive en la misma carpeta, con el nombre del archivo más ".tmp"
/// (el .gitignore de la biblioteca ya lo ignora), y recién completo reemplaza al original en una sola operación. Si algo falla queda
/// el original y el temporal se borra. File.Replace falla cuando el destino no existe: el primer guardado mueve (01 §9).
/// </summary>
public static class AtomicWrite
{
    public static void Replace(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Un temporal que no se pudo borrar no es peor que el original que se conservó; el próximo guardado lo pisa.
        }
    }
}
