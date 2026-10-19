using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que el catálogo real necesita para las cadenas, armado para una prueba: ventanas de mentira y page.go que no hace nada.</summary>
internal static class TestChains
{
    public static Chains For(Library library, Log log, IWindows? windows = null, Func<string, Failure?>? goToPage = null, TimeProvider? clock = null) =>
        new(library, windows ?? new FakeWindows(), new ScriptRunner(library, log, clock ?? TimeProvider.System, null), goToPage ?? (_ => null));
}
