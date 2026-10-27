using Sietom.AccessControl;
using Xunit;

public sealed class InstallationRetryTests
{
    [Theory]
    [InlineData("ApiHttp500")]
    [InlineData("ApiHttp503")]
    [InlineData("ApiHttp429")]
    [InlineData("TokenHttp502")]
    [InlineData("TokenHttp408")]
    public void UnavailableApiAllowsBackgroundRetries(string code)
        =>Assert.Equal(2,Worker.OnceExitCode(new SyncException(code)));

    [Theory]
    [InlineData("ApiHttp401")]
    [InlineData("ApiHttp403")]
    [InlineData("TokenHttp400")]
    [InlineData("InvalidCardRecord")]
    [InlineData("DeactivationLimitExceeded")]
    public void PermissionsAndInvalidDataStillStopInstallation(string code)
        =>Assert.Equal(1,Worker.OnceExitCode(new SyncException(code)));

    [Fact]
    public void NetworkFailureAllowsBackgroundRetries()=>Assert.Equal(2,Worker.OnceExitCode(new HttpRequestException()));

    [Fact]
    public void ExplicitForbiddenIsNotTreatedAsNetworkFailure()=>Assert.Equal(1,Worker.OnceExitCode(new HttpRequestException("Forbidden",null,System.Net.HttpStatusCode.Forbidden)));

    [Fact]
    public void InvalidSettingsStillStopInstallation()=>Assert.Equal(1,Worker.OnceExitCode(new InvalidOperationException()));
}
