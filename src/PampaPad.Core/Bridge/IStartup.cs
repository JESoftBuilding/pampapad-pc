namespace PampaPad.Core;

/// <summary>
/// El arranque con Windows (05 §4.5, tarjeta Programa). Lo implementa la cáscara (App/StartWithWindows.cs) con la clave Run del usuario;
/// las pruebas usan uno de mentira. IsEnabled es null en una copia sin instalar: ahí no se toca el registro de Windows.
/// </summary>
public interface IStartup
{
    bool? IsEnabled { get; }

    Result Set(bool on);
}
