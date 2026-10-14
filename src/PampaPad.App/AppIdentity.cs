namespace PampaPad.App;

/// <summary>Cómo se llama esta copia. En Debug lleva el sufijo -dev para convivir con la instalada (05 §10).</summary>
internal static class AppIdentity
{
#if DEBUG
    public const string Name = "PampaPad-dev";
    public const string Title = "PampaPad (dev)";
    public const bool Dev = true;
#else
    public const string Name = "PampaPad";
    public const string Title = "PampaPad";
    public const bool Dev = false;
#endif

    public static string Version { get; } = typeof(AppIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
}
