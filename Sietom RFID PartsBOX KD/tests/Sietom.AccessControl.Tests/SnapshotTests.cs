using System.Text;
using Sietom.AccessControl;
using Xunit;

public class SnapshotTests
{
    private const string Card = """{"employeeId":1578,"firstName":"Jan","lastName":"Kowalski","siteCode":240,"cardNumber":12345}""";

    [Fact]
    public void ContractExampleMapsCompoundCardKey()
    {
        var card = Assert.Single(Snapshot.Parse(Encoding.UTF8.GetBytes($"[{Card}]"), 1, false));
        Assert.Equal(240, card.SiteCode);
        Assert.Equal(12345L, card.CardNumber);
        Assert.Equal(1578L, card.EmployeeId);
    }

    [Theory]
    [InlineData("[]", 0, "EmptySnapshotBlocked")]
    [InlineData("[]", 1, "SnapshotCountMismatch")]
    [InlineData("null", 0, "SnapshotCountMismatch")]
    [InlineData("[null]", 1, "InvalidCardRecord")]
    [InlineData("[{\"firstName\":\"Jan\"}]", 1, "InvalidSnapshotJson")]
    [InlineData("{\"content\":[]}", 0, "InvalidSnapshotJson")]
    public void RejectsUnsafeOrIncompleteSnapshots(string json, long total, string code)
    {
        Assert.Equal(code, Assert.Throws<SyncException>(() => Snapshot.Parse(Encoding.UTF8.GetBytes(json), total, false)).Code);
    }

    [Fact]
    public void CardWithDifferentEmployeesIsQuarantined()
    {
        var json = $"[{Card},{Card.Replace("1578", "2048")}]";
        var result=Snapshot.ParseImport(Encoding.UTF8.GetBytes(json),2,false);
        Assert.Empty(result.Cards);
        Assert.Equal(new CardConflict(240,12345),Assert.Single(result.Conflicts));
        Assert.Equal(2,result.SourceRecordCount);
    }

    [Fact]
    public void IdenticalDuplicatesAreCollapsedAfterCheckingRawResponseCount()
    {
        var bytes=Encoding.UTF8.GetBytes($"[{Card},{Card}]");
        Assert.Single(Snapshot.Parse(bytes,2,false));
        Assert.Equal("SnapshotCountMismatch",Assert.Throws<SyncException>(()=>Snapshot.Parse(bytes,1,false)).Code);
    }

    [Fact]
    public void ConflictingNamesOnTheSameCardCannotBeSilentlyDiscarded()
    {
        var other=Card.Replace("Jan","Piotr");
        var result=Snapshot.ParseImport(Encoding.UTF8.GetBytes($"[{Card},{other},{Card}]"),3,false);
        Assert.Empty(result.Cards);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public void ReportCountsDuplicatesOfEitherOwnerWithoutAuthorizingTheirCard()
    {
        var other=Card.Replace("1578","2048");
        var result=Snapshot.ParseImport(Encoding.UTF8.GetBytes($"[{Card},{other},{other},{Card}]"),4,false);
        Assert.Empty(result.Cards);
        Assert.Single(result.Conflicts);
        Assert.Equal(2,result.IdenticalDuplicateCount);
    }

    [Fact]
    public void SameNumberInDifferentSiteIsDistinct()
    {
        var json = $"[{Card},{Card.Replace("240", "241")}]";
        Assert.Equal(2, Snapshot.Parse(Encoding.UTF8.GetBytes(json), 2, false).Count);
    }

    [Fact]
    public void EmptySnapshotNeedsExplicitOptIn()
    {
        Assert.Empty(Snapshot.Parse(Encoding.UTF8.GetBytes("[]"), 0, true));
    }

    [Fact]
    public void BigintCardNumberIsNotTruncated()
    {
        var json = $"[{Card.Replace("12345", "9223372036854775807")}]";
        Assert.Equal(long.MaxValue, Snapshot.Parse(Encoding.UTF8.GetBytes(json), 1, false)[0].CardNumber);
    }

    [Fact]
    public void SeveralCardsForTheSamePersonAreAccepted()
    {
        Assert.Equal(2, Snapshot.Parse(Encoding.UTF8.GetBytes($"[{Card},{Card.Replace("12345", "67890")}]"), 2, false).Count);
    }

    [Fact]
    public void ConflictingNamesForTheSamePersonAreRejected()
    {
        var second = Card.Replace("12345", "67890").Replace("Jan", "Piotr");
        Assert.Equal("ConflictingEmployeeNames", Assert.Throws<SyncException>(() => Snapshot.Parse(Encoding.UTF8.GetBytes($"[{Card},{second}]"), 2, false)).Code);
    }

    [Theory]
    [InlineData("1578", "-1")]
    [InlineData("240", "-1")]
    [InlineData("12345", "-1")]
    [InlineData("1578", "-9223372036854775808")]
    [InlineData("240", "-2147483648")]
    [InlineData("12345", "-9223372036854775808")]
    public void SignedIdentifiersFromTheContractAreAccepted(string value, string replacement)
    {
        var result=Assert.Single(Snapshot.Parse(Encoding.UTF8.GetBytes($"[{Card.Replace(value, replacement)}]"),1,false));
        Assert.Equal(long.Parse(replacement),value=="1578"?result.EmployeeId:value=="240"?result.SiteCode:result.CardNumber);
    }

    [Fact]
    public void NameTooLongForEmployeesIsRejectedWithoutTruncation()
    {
        var json = Card.Replace("Kowalski", new string('x', 150));
        Assert.Equal("InvalidCardRecord", Assert.Throws<SyncException>(() => Snapshot.Parse(Encoding.UTF8.GetBytes($"[{json}]"), 1, false)).Code);
    }

    [Theory]
    [InlineData("Jan", "", "FirstName:Required")]
    [InlineData("Kowalski", " ", "LastName:Required")]
    public void ValidationReportsOnlyPositionAndRule(string original,string replacement,string rule)
    {
        var error = Assert.Throws<SyncException>(() => Snapshot.Parse(Encoding.UTF8.GetBytes($"[{Card.Replace(original,replacement)}]"),1,false));
        Assert.Equal("InvalidCardRecord",error.Code);
        Assert.Contains("RecordPosition=1",error.Diagnostic);
        Assert.Contains(rule,error.Diagnostic);
        Assert.DoesNotContain("Kowalski",error.Diagnostic);
        Assert.DoesNotContain("12345",error.Diagnostic);
        Assert.DoesNotContain("1578",error.Diagnostic);
    }
}
