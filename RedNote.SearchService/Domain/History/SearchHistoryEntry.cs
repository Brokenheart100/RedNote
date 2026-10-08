namespace RedNote.SearchService.Domain.History;

public sealed class SearchHistoryEntry
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Keyword { get; private set; } = "";
    public string NormalizedKeyword { get; private set; } = "";
    public DateTimeOffset LastSearchedAtUtc { get; private set; }
}
