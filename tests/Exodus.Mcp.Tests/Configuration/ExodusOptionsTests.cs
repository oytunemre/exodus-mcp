using Exodus.Mcp.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Exodus.Mcp.Tests.Configuration;

public class ExodusOptionsTests
{
    private static IConfiguration Build(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Parses_AllValues_ProfileCaseInsensitive()
    {
        var options = ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", "http://h:1"),
            ("EXODUS_PROFILE", "SELLER"),
            ("EXODUS_USERNAME", "user"),
            ("EXODUS_PASSWORD", "pw")));

        options.Profile.Should().Be(ExodusProfile.Seller);
        options.BaseUrl.Should().Be(new Uri("http://h:1/"));
        options.Username.Should().Be("user");
        options.Password.Should().Be("pw");
    }

    [Fact]
    public void BaseUrl_DefaultsToLocalhost5013_AndAlwaysEndsWithSlash()
    {
        var defaulted = ExodusOptions.FromConfiguration(Build(
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));
        defaulted.BaseUrl.ToString().Should().Be("http://localhost:5013/");

        var withPath = ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", "http://h:1/x"),
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));
        withPath.BaseUrl.ToString().Should().Be("http://h:1/x/");
    }

    [Fact]
    public void ReportsEveryProblemAtOnce()
    {
        var act = () => ExodusOptions.FromConfiguration(Build());

        var message = act.Should().Throw<ExodusConfigurationException>().Which.Message;
        message.Should().Contain("EXODUS_PROFILE");
        message.Should().Contain("EXODUS_USERNAME tanımlı değil.");
        message.Should().Contain("EXODUS_PASSWORD tanımlı değil.");
        message.Should().NotContain("EXODUS_BASE_URL");
    }

    [Fact]
    public void RejectsNonHttpBaseUrl()
    {
        var act = () => ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", "ftp://x"),
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));

        act.Should().Throw<ExodusConfigurationException>()
            .Which.Message.Should().Contain("EXODUS_BASE_URL geçerli bir http(s) adresi olmalı (gelen: 'ftp://x').");
    }

    [Fact]
    public void RejectsBaseUrlWithUserInfo_WithoutEchoingSecret()
    {
        var act = () => ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", "http://user:S3cret!@h:1/"),
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));

        var message = act.Should().Throw<ExodusConfigurationException>().Which.Message;
        message.Should().Contain("EXODUS_BASE_URL kullanıcı adı/şifre, query ya da fragment içermemeli; yalnızca şema, host, port ve yol kullanın.");
        message.Should().NotContain("S3cret!");
    }

    [Theory]
    [InlineData("ftp://user:S3cret!@h:1/")]
    [InlineData("http://user:S3cret!@")]
    public void RejectsInvalidBaseUrlWithCredentials_WithoutEchoingSecret(string baseUrl)
    {
        var act = () => ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", baseUrl),
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));

        var message = act.Should().Throw<ExodusConfigurationException>().Which.Message;
        message.Should().Contain("EXODUS_BASE_URL").And.NotContain("S3cret!");
    }

    [Theory]
    [InlineData("http://h:1/x?y=1")]
    [InlineData("http://h:1/#f")]
    public void RejectsBaseUrlWithQueryOrFragment(string baseUrl)
    {
        var act = () => ExodusOptions.FromConfiguration(Build(
            ("EXODUS_BASE_URL", baseUrl),
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "p")));

        act.Should().Throw<ExodusConfigurationException>()
            .Which.Message.Should().Contain("EXODUS_BASE_URL kullanıcı adı/şifre, query ya da fragment içermemeli; yalnızca şema, host, port ve yol kullanın.");
    }

    [Fact]
    public void ToString_NeverContainsPassword()
    {
        var options = ExodusOptions.FromConfiguration(Build(
            ("EXODUS_PROFILE", "customer"), ("EXODUS_USERNAME", "u"), ("EXODUS_PASSWORD", "S3cret!")));

        options.ToString().Should().NotContain("S3cret!");
        options.ToString().Should().Contain("Profile=Customer").And.Contain("Username=u");
    }
}
