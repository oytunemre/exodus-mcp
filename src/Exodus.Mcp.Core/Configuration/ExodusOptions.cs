using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Exodus.Mcp.Core.Configuration;

// Not a record: the compiler-generated ToString would print the password.
public sealed class ExodusOptions
{
    public const string DefaultBaseUrl = "http://localhost:5013";

    private const string BaseUrlPartsProblem =
        "EXODUS_BASE_URL kullanıcı adı/şifre, query ya da fragment içermemeli; yalnızca şema, host, port ve yol kullanın.";

    public required Uri BaseUrl { get; init; }
    public required ExodusProfile Profile { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    public static ExodusOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        var rawProfile = Read(configuration, "EXODUS_PROFILE");
        ExodusProfile profile = default;
        if (string.Equals(rawProfile, "customer", StringComparison.OrdinalIgnoreCase))
            profile = ExodusProfile.Customer;
        else if (string.Equals(rawProfile, "seller", StringComparison.OrdinalIgnoreCase))
            profile = ExodusProfile.Seller;
        else
            problems.Add($"EXODUS_PROFILE 'customer' veya 'seller' olmalı (gelen: '{rawProfile}').");

        var username = Read(configuration, "EXODUS_USERNAME");
        if (username is null)
            problems.Add("EXODUS_USERNAME tanımlı değil.");

        var password = Read(configuration, "EXODUS_PASSWORD");
        if (password is null)
            problems.Add("EXODUS_PASSWORD tanımlı değil.");

        var rawBaseUrl = Read(configuration, "EXODUS_BASE_URL") ?? DefaultBaseUrl;
        Uri? baseUrl = null;
        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out baseUrl))
        {
            // An '@' may mean credentials: never echo such a value.
            problems.Add(rawBaseUrl.Contains('@') ? BaseUrlPartsProblem : InvalidBaseUrlProblem(rawBaseUrl));
        }
        else if (baseUrl.UserInfo.Length > 0 || baseUrl.Query.Length > 0 || baseUrl.Fragment.Length > 0)
        {
            // The base URL is printed by whoami and by error messages, so it must not carry a password.
            problems.Add(BaseUrlPartsProblem);
        }
        else if (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add(InvalidBaseUrlProblem(rawBaseUrl));
        }

        if (problems.Count > 0)
            throw new ExodusConfigurationException(string.Join(Environment.NewLine, problems));

        var builder = new UriBuilder(baseUrl!);
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";

        return new ExodusOptions
        {
            BaseUrl = builder.Uri,
            Profile = profile,
            Username = username!,
            Password = password!
        };
    }

    public override string ToString() => $"Profile={Profile}, BaseUrl={BaseUrl}, Username={Username}";

    private static string InvalidBaseUrlProblem(string rawBaseUrl)
        => $"EXODUS_BASE_URL geçerli bir http(s) adresi olmalı (gelen: '{rawBaseUrl}').";

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
