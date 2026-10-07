namespace Prompuff.Domain.Entities;

public sealed record Tag
{
    public required Guid Id { get; init; }

    /// <summary>Always the normalized form; see <see cref="ValueObjects.TagName"/>.</summary>
    public required string Name { get; init; }
}
