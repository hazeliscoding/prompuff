namespace Prompuff.Domain.ValueObjects;

/// <summary>Usefulness from 1 to 5. Null means unrated.</summary>
public static class Rating
{
    public const int Min = 1;
    public const int Max = 5;

    public static bool IsValid(int? value) => value is null or (>= Min and <= Max);

    public static int? Validate(int? value) =>
        IsValid(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Rating must be between {Min} and {Max}.");
}
