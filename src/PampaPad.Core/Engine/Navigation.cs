namespace PampaPad.Core;

/// <summary>Cycle son los ids de las páginas con show, en orden: lo usan next, previous y, más adelante, la perilla.</summary>
public sealed record NavigationInput(string BasePage, string? HeldLayer, RecordingState Recording, string MainPage, string? Destination, IReadOnlyList<string> Cycle);

public sealed record NavigationResult(string ActivePage, string BasePage, Failure? Failure);

/// <summary>
/// El único dueño de qué página rige (02 §7). Es una función pura: no guarda nada.
/// La regla de grabación, en una línea: con una toma en curso la base es la principal y nada la cambia.
/// </summary>
public static class Navigation
{
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Main = "main";

    public static NavigationResult Resolve(NavigationInput input)
    {
        string basePage = input.BasePage;
        Failure? failure = null;
        if (input.Recording != RecordingState.Stopped)
        {
            // Reglas 1 y 2: grabando, en pausa, arrancando o deteniendo se vuelve a la principal, y un pedido de ir a otra página falla.
            basePage = input.MainPage;
            if (input.Destination is not null)
            {
                failure = new Failure(ErrorCodes.NavigationBlocked, "Con una toma en curso no se cambia de página.");
            }
        }
        else if (input.Destination is { } destination)
        {
            basePage = destination switch
            {
                Main => input.MainPage,
                Next => Walk(input, +1),
                Previous => Walk(input, -1),
                _ => destination,
            };
        }

        // Reglas 3 y 4: la capa rige mientras se mantiene la tecla del módulo, también grabando, y no toca la base.
        return new NavigationResult(input.HeldLayer ?? basePage, basePage, failure);
    }

    // Regla 5: después de la última sigue la primera. Desde una página que no está en el recorrido, next va a la primera y previous a la última.
    private static string Walk(NavigationInput input, int direction)
    {
        int count = input.Cycle.Count;
        if (count == 0)
        {
            return input.BasePage;
        }

        int at = -1;
        for (int i = 0; i < count; i++)
        {
            if (input.Cycle[i] == input.BasePage)
            {
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            return direction > 0 ? input.Cycle[0] : input.Cycle[count - 1];
        }

        return input.Cycle[((at + direction) % count + count) % count];
    }
}
