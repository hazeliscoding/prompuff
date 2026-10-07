namespace Prompuff.Application.DTOs;

public sealed record CollectionSummary(Guid Id, string Name, int PromptCount);

public sealed record TagSummary(string Name, int PromptCount);

public sealed record LibraryCounts(int All, int Favorites, int Uncategorized, int Versions);
