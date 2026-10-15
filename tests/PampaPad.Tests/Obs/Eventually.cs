using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace PampaPad.Tests;

// Esperas de las pruebas que usan un socket real. El tiempo del producto corre con FakeTimeProvider; el
// tiempo real de acá es sólo la red de seguridad para que una prueba rota falle en vez de colgarse.
internal static class Eventually
{
    private static readonly TimeSpan RealLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(25);

    // Espera, sin mover el reloj falso, a que se cumpla la condición.
    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var started = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(started) > RealLimit)
            {
                Assert.Fail($"No pasó a tiempo: {what}");
            }

            await Task.Delay(Settle);
        }
    }

    // Avanza el reloj falso de a pasos, dejando correr el socket entre paso y paso, hasta que se cumpla.
    public static async Task AdvancingAsync(FakeTimeProvider clock, TimeSpan step, Func<bool> condition, string what)
    {
        var started = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(started) > RealLimit)
            {
                Assert.Fail($"No pasó a tiempo: {what}");
            }

            clock.Advance(step);
            await Task.Delay(Settle);
        }
    }

    // Avanza exactamente esa cantidad de pasos, dejando correr el socket después de cada uno.
    public static async Task StepsAsync(FakeTimeProvider clock, TimeSpan step, int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            clock.Advance(step);
            await Task.Delay(Settle);
        }
    }
}
