using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Exodus.Mcp.Core;

public static class ServiceCollectionExtensions
{
    public const string ServerName = "exodus-mcp";
    public const string ServerVersion = "0.1.0";

    private const string CustomerInstructions =
        "Exodus pazaryerinde müşteri hesabıyla alışveriş araçları. " +
        "Sipariş vermeden önce sepeti, teslimat adresini ve toplam tutarı özetleyip kullanıcıdan açık onay al. " +
        "Kart bilgisi asla isteme; ödeme kapıda ödeme olarak kaydedilir.";

    private const string SellerInstructions =
        "Exodus pazaryerinde satıcı hesabıyla listing ve sipariş yönetimi araçları. " +
        "Bir siparişi kargoya verirken taşıyıcı firma ve takip numarası zorunludur; bilinmiyorsa kullanıcıya sor.";

    /// <summary>
    /// Registers the Exodus HTTP stack (<see cref="ExodusHttpServiceCollectionExtensions.AddExodusApi"/>) and an MCP
    /// server whose tools, prompts and resources depend on <see cref="ExodusOptions.Profile"/>. The caller adds the
    /// transport (<c>WithStdioServerTransport</c> or <c>WithStreamServerTransport</c>) to the returned builder.
    /// </summary>
    public static IMcpServerBuilder AddExodusMcp(this IServiceCollection services, ExodusOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddExodusApi(options);

        var builder = services.AddMcpServer(server =>
        {
            server.ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion };
            server.ServerInstructions = Instructions(options.Profile);
        });

        builder.WithTools<CommonTools>();

        // Each WithTools<T>/WithPrompts<T>/WithResources<T> registers only that type, so a profile exposes nothing else.
        switch (options.Profile)
        {
            case ExodusProfile.Customer:
                builder.WithTools<CustomerTools>();
                // Customer prompts and resources go here.
                break;
            case ExodusProfile.Seller:
                builder.WithTools<SellerTools>();
                // Seller prompts and resources go here.
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Profile, "Unknown Exodus profile.");
        }

        return builder;
    }

    private static string Instructions(ExodusProfile profile) => profile switch
    {
        ExodusProfile.Customer => CustomerInstructions,
        ExodusProfile.Seller => SellerInstructions,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown Exodus profile."),
    };
}
