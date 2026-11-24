using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

namespace Exodus.Mcp.Core.Prompts;

/// <summary>Prompts of the customer profile. A string result is sent to the client as a single user message.</summary>
[McpServerPromptType]
public sealed class CustomerPrompts
{
    [McpServerPrompt(Name = "shopping_assistant")]
    [Description("Alışveriş asistanı: ürün arar, karşılaştırır, sepete ekler ve açık onayla sipariş verir.")]
    public string ShoppingAssistant(
        [Description("Kullanıcının alışveriş hedefi (ör. 'bütçesi 2000 TL olan bir kulaklık'). Boş bırakılabilir.")] string? goal = null)
    {
        var text =
            "Exodus pazaryerinde bana alışverişte yardım et. Şu adımları izle:\n" +
            "1. search_listings ile ürünleri ara (gerekirse list_categories ile kategori bul).\n" +
            "2. Uygun ürünleri fiyat, stok, durum ve satıcıya göre karşılaştır; get_listing ile ayrıntıya bak.\n" +
            "3. Benim seçtiğim ürünü add_to_cart ile sepete ekle.\n" +
            "4. list_my_addresses ile teslimat adresini teyit et.\n" +
            "5. get_cart ile sepeti göster; sepet, adres ve toplam tutarın özetini sun.\n" +
            "6. Ben açıkça onay vermeden asla place_order çağırma; özetin ardından onayımı (onay) bekle.\n" +
            "Kart bilgisi isteme; ödeme yalnızca kapıda ödemedir.";

        return string.IsNullOrWhiteSpace(goal) ? text : $"{text}\n\nHedefim: {goal.Trim()}";
    }
}

/// <summary>Prompts of the seller profile.</summary>
[McpServerPromptType]
public sealed class SellerPrompts(TimeProvider time)
{
    [McpServerPrompt(Name = "seller_daily_report")]
    [Description("Satıcı günlük raporu: düşük/biten stok, bekleyen siparişler ve son 7 günün satış özeti.")]
    public string SellerDailyReport()
    {
        var today = time.GetUtcNow().UtcDateTime.Date;
        var to = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
            "Exodus satıcı hesabım için günlük rapor hazırla. Önce şu çağrıları yap:\n" +
            "1. list_my_listings(stockFilter=\"low\")\n" +
            "2. list_my_listings(stockFilter=\"out\")\n" +
            "3. list_seller_orders(status=\"Pending\")\n" +
            $"4. sales_summary(fromDate=\"{from}\", toDate=\"{to}\")\n" +
            "Ardından sonuçlara dayanarak kısa bir Türkçe rapor yaz: düşük ve biten stoklar, bekleyen siparişler, son 7 günün satış özeti ve önerilen aksiyonlar.";
    }
}
