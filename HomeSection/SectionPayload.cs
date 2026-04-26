// HomeSection/SectionPayload.cs
namespace Jellyfin.Plugin.Watchlist.HomeSection;

/// <summary>
/// Mirrors Jellyfin.Plugin.HomeScreenSections.Model.Dto.HomeScreenSectionPayload.
/// HSS deserialises into whatever type the handler method signature declares —
/// no compile-time reference to the HSS assembly is needed.
/// </summary>
public sealed class SectionPayload
{
    public Guid    UserId         { get; set; }
    public string? AdditionalData { get; set; }
}
