using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sietom.AccessControl;

public sealed record CardUser(
    [property: JsonRequired] long EmployeeId,
    [property: JsonRequired] string FirstName,
    [property: JsonRequired] string LastName,
    [property: JsonRequired] int SiteCode,
    [property: JsonRequired] long CardNumber);

public sealed class SyncException(string code, string? diagnostic = null) : Exception(code)
{
    public string Code { get; } = code;
    public string? Diagnostic { get; } = diagnostic;
}

public sealed record CardConflict(int SiteCode,long CardNumber);
public sealed record CardImportSnapshot(IReadOnlyList<CardUser> Cards,IReadOnlyList<CardConflict> Conflicts,int SourceRecordCount,int IdenticalDuplicateCount);

public static class Snapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyList<CardUser> Parse(byte[] body, long total, bool allowEmpty)
        => ParseImport(body,total,allowEmpty).Cards;

    public static CardImportSnapshot ParseImport(byte[] body, long total, bool allowEmpty)
    {
        CardUser[]? cards;
        try { cards = JsonSerializer.Deserialize<CardUser[]>(body, JsonOptions); }
        catch (JsonException) { throw new SyncException("InvalidSnapshotJson"); }
        if (cards is null || cards.LongLength != total) throw new SyncException("SnapshotCountMismatch");
        if (cards.Length == 0 && !allowEmpty) throw new SyncException("EmptySnapshotBlocked");
        var keys = new Dictionary<(int, long), (CardUser Card, int Position)>();
        var unique = new List<CardUser>();
        var conflictingKeys = new HashSet<(int,long)>();
        var duplicates = 0;
        var seenRecords = new HashSet<CardUser>();
        var people = new Dictionary<long, (string FirstName, string LastName)>();
        for (var index = 0; index < cards.Length; index++)
        {
            var card = cards[index];
            string? rule = card is null ? "Record:Null" :
                string.IsNullOrWhiteSpace(card.FirstName) ? "FirstName:Required" :
                string.IsNullOrWhiteSpace(card.LastName) ? "LastName:Required" :
                card.FirstName.Length > 200 ? "FirstName:LengthAbove200" :
                card.LastName.Length > 200 ? "LastName:LengthAbove200" :
                card.FirstName.Length + 1 + card.LastName.Length > 150 ? "Employees.Name:LengthAbove150" : null;
            if (rule is not null)
                throw new SyncException("InvalidCardRecord", $"RecordPosition={index + 1}; Rule={rule}. No employee values are logged.");
            // The null record case was rejected above.
            if (card is null) throw new SyncException("InvalidCardRecord");
            if(!seenRecords.Add(card))duplicates++;
            var key = (card.SiteCode, card.CardNumber);
            if (keys.TryGetValue(key, out var existing))
            {
                if (existing.Card != card) conflictingKeys.Add(key);
                continue;
            }
            keys.Add(key, (card,index + 1));
        }
        foreach(var entry in keys)
        {
            if(conflictingKeys.Contains(entry.Key))continue;
            var card=entry.Value.Card;
            var name = (card.FirstName, card.LastName);
            if (people.TryGetValue(card.EmployeeId, out var previous) && previous != name)
                throw new SyncException("ConflictingEmployeeNames");
            people[card.EmployeeId] = name;
            unique.Add(card);
        }
        return new(unique,conflictingKeys.Select(k=>new CardConflict(k.Item1,k.Item2)).ToArray(),cards.Length,duplicates);
    }
}
