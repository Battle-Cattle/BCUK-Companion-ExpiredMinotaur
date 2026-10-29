namespace BCUKCompanion.ExpiredMinotaur.Tests;

/// <summary>An <see cref="ActionsConfigStore"/> in a data folder of its own, deleted on dispose.</summary>
internal sealed class TempActionsStore : IDisposable
{
    public ActionsConfigStore Store { get; } =
        new($"BCUKCompanion.ExpiredMinotaur.Tests.{Guid.NewGuid():N}", Program.CreateRegistry());

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(Store.ConfigFilePath)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
