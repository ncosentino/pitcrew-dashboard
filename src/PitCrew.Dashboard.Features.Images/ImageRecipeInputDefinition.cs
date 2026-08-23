namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Describes one declared image-workflow input.
/// </summary>
/// <param name="Name">Stable workflow input key.</param>
/// <param name="Type">Closed primitive type: string, integer, number, or boolean.</param>
/// <param name="Required">Whether later build requests must supply a value.</param>
/// <param name="MaxLength">Maximum string length when <paramref name="Type" /> is string.</param>
/// <param name="AllowedValues">Optional closed string enum when <paramref name="Type" /> is string.</param>
public sealed record ImageRecipeInputDefinition(
    string Name,
    string Type,
    bool Required,
    int? MaxLength,
    IReadOnlyList<string>? AllowedValues);
