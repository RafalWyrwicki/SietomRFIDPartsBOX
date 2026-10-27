using Sietom.AccessControl;
using Xunit;

public sealed class SettingsTests
{
    private static Settings Configuration(string database = "SietomPartsBox", bool integrated = true, bool encrypt = false) => new()
    {
        OAuth = new() { TokenUrl = "https://sso.tarczynski.pl/realms/software/protocol/openid-connect/token" },
        Sql = new() { ConnectionString = $"Server=localhost;Database={database};Integrated Security={integrated};Encrypt={encrypt};Connect Timeout=5" }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDatabaseSupportsClientEncryptionSettings(bool encrypt) => Configuration(encrypt: encrypt).Validate();

    [Fact]
    public void SeparateOldDatabaseIsRejected() => Assert.Throws<InvalidOperationException>(() => Configuration("SietomAccessControl").Validate());

    [Fact]
    public void SqlPasswordLoginIsRejected() => Assert.Throws<InvalidOperationException>(() => Configuration(integrated: false).Validate());
}
