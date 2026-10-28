using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El arranque con Windows de mentira: anota cada Set. Con null es una copia sin instalar.</summary>
internal sealed class FakeStartup(bool? enabled) : IStartup
{
    public List<bool> Calls { get; } = [];

    public bool? IsEnabled { get; private set; } = enabled;

    public Result Set(bool on)
    {
        Calls.Add(on);
        IsEnabled = on;
        return Result.Success;
    }
}
