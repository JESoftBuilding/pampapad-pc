using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsItemsTests
{
    private const string CameraUuid = "3da67a13-6704-4c87-9e09-c8be28efdf20";
    private const string BancoUuid = "900ebc25-03ef-443e-80cc-2ad8991268ff";
    private const string Gone = "00000000-0000-0000-0000-000000000000";

    private static readonly IReadOnlyList<ObsItem> Scenes = [new(CameraUuid, "Cámara"), new(BancoUuid, "Banco")];

    [Fact]
    public void The_uuid_wins_even_if_the_name_changed()
    {
        Assert.Equal(new ObsItem(BancoUuid, "Banco"), ObsItems.Resolve(new ObsRef(BancoUuid, "Banco viejo"), Scenes));
    }

    [Fact]
    public void A_uuid_that_is_gone_falls_back_to_the_name()
    {
        Assert.Equal(new ObsItem(CameraUuid, "Cámara"), ObsItems.Resolve(new ObsRef(Gone, "Cámara"), Scenes));
    }

    [Fact]
    public void Written_by_hand_without_uuid_it_goes_by_name()
    {
        Assert.Equal(new ObsItem(BancoUuid, "Banco"), ObsItems.Resolve(new ObsRef(null, "Banco"), Scenes));
    }

    [Theory]
    [InlineData(null, "Pantalla")]
    [InlineData(Gone, "Pantalla")]
    [InlineData(Gone, "")]
    [InlineData(null, "banco")]                 // los nombres de OBS distinguen mayúsculas
    public void What_is_not_in_the_list_is_not_found(string? uuid, string name)
    {
        Assert.Null(ObsItems.Resolve(new ObsRef(uuid, name), Scenes));
    }

    [Fact]
    public void Adding_removing_or_renaming_what_is_not_there_leaves_the_same_list()
    {
        Assert.Same(Scenes, ObsItems.With(Scenes, new ObsItem(BancoUuid, "Banco")));
        Assert.Same(Scenes, ObsItems.Without(Scenes, Gone));
        Assert.Same(Scenes, ObsItems.Renamed(Scenes, Gone, "Otra"));
        Assert.Same(Scenes, ObsItems.Without(Scenes, null));
    }
}
