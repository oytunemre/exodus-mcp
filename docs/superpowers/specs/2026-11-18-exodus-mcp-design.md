# Exodus MCP — Tasarım ve Uygulama Planı

## Bağlam

Oytun, **Exodus** (github.com/oytunemre/Exodus — ASP.NET Core 10 marketplace REST API'si: EF Core, SQL Server, JWT + refresh token, iyzico 3DS, satıcı bazlı sipariş bölme) için bir **MCP sunucusu** yazmak istiyor. Amaç **portföy/CV**: Junior .NET Backend başvurularında "API'mi yapay zekâ ajanlarının kullanabileceği hale getirdim" diyebilmek. Başarı ölçütü: Claude'a düz Türkçe yazınca müşteri ve satıcı akışlarının uçtan uca çalışması, temiz mimari, testler, güçlü bir README ve demo GIF'i.

Keşif sırasında Exodus'ta MCP'yi doğrudan etkileyen güvenlik açıkları ve eksikler bulundu. Oytun **"önce Exodus'u toparla"** dedi. Bu yüzden iş iki alt projeye ayrılıyor: **A: Exodus'u toparlama** → **B: Exodus MCP**.

## Alınan kararlar (Oytun'la)

| Konu | Karar |
|---|---|
| Amaç | Portföy / CV projesi |
| Dil | C# / .NET 10, resmi `ModelContextProtocol` C# SDK'si (stabil 2.x) |
| Çalışma şekli | Önce lokal stdio + video demo (Aşama 1), sonra HTTP ile deploy (Aşama 2) |
| Roller | Müşteri + Satıcı (admin yok) |
| Exodus | MCP'den önce toparlanacak: PR #4 incelenip merge edilecek, kalan açıklar kapatılacak |
| Repo | MCP için ayrı repo (`~/Projects/Exodus MCP` → GitHub `exodus-mcp`). MCP, Exodus'a HTTP ile bağlanan bir istemci; Exodus'un koduna referans vermiyor |
| Ödeme | MCP **asla kart bilgisi taşımıyor**. iyzico/kartlı ödeme MCP'nin dışında kalıyor |

## Mevcut durum (bulgular)

- `main` dalının son commit'i 2026-03. **PR #4** (`devin/1789482413-bug-fixes`, 6 commit, 88 dosya, açık) şunları zaten düzeltiyor: kayıtta rol her zaman `Customer` (herkes Admin olamıyor); seed kullanıcıların şifreleri BCrypt'li (`Demo123!`; 2 satıcı: zeynep@, mehmet@; 3 müşteri: ahmet@, elif@, can@ — hepsi `@test.com`, `EmailVerified = true`); sepette yanlış kullanıcı için 500 → 403; listing sahiplik kontrolü; global rate limit (100/dk/IP); ProblemDetails formatında 401/403 yanıtları; sayfalama doğrulaması. **Migration geçmişini tek bir `Baseline` migration'ına sıkıştırıyor**, yani mevcut lokal veritabanları sıfırdan oluşturulmalı.
- PR #4'ten sonra **hâlâ açık kalanlar**:
  - 🔴 `POST /api/payment/intents/{id}/simulate-success` ve `simulate-fail` giriş yapmış herkese açık (`Controllers/PaymentController.cs:126`).
  - 🔴 Eski `Controllers/SellerController.cs`: `GET {sellerId}/orders` çağıranı kontrol etmiyor (IDOR); `PATCH orders/{id}/ship` ve `/deliver` sahiplik kontrolü yapmıyor, yani her satıcı başka bir satıcının siparişini kargolayabiliyor. Bu uçlar `SellerOrderController` ve `ShipmentsController` ile mükerrer.
  - 🟠 Public `GET /api/listings` ve `GET /api/product` uçlarında filtre ve sayfalama yok. `ListingSearchDto` ve `ProductSearchDto` tanımlı ama hiçbir controller kullanmıyor. Filtreleme mantığı admin tarafında var: `AdminListingController` (minPrice, maxPrice, search, condition, categoryId, sort, paging).
  - 🟠 Seeder (`Data/DbSeeder.cs`): listing'lerin satıcısını tüm kullanıcılar arasından `SellerIndex` ile seçiyor, bu yüzden listing'ler **müşterilere** atanıyor. Ayrıca sadece 5 ürün var, kategori ataması ve müşteri adresi yok (checkout `shippingAddressId` istiyor).
  - 🟡 `appsettings.json`: iyzico callback ve Email `BaseUrl` yanlış port ve yanlış yol kullanıyor (`localhost:5000/api/payments/...`; doğrusu `:5013/api/payment/3ds-callback`). README'deki route'lar koddakilerle uyuşmuyor (`/api/products` vs `/api/product`, `refresh-token` vs `refresh` vb.). CI yok.
- Bu makinede **`dotnet` ve `docker` kurulu değil** (arm64). Node v24 var (MCP Inspector için yeterli).

---

## Ön koşul — Geliştirme ortamı

1. .NET 10 SDK: `brew install --cask dotnet-sdk` (kurulumdan önce Oytun'a sorulacak).
2. Container runtime: OrbStack (önerilen, Mac'te hafif) veya Docker Desktop. İçinde SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest`, arm64'te Rosetta emülasyonuyla).
3. Exodus'u `~/Projects/Exodus` klasörüne `git clone` et. Connection string'i ve JWT secret'ı (≥32 karakter; PR #4 bunu zorunlu kılıyor) `dotnet user-secrets` ile ver.

## Alt proje A — Exodus'u toparlama (Exodus reposunda, ayrı dalda)

Her düzeltmenin bir regresyon testi olacak. Testler mevcut `Exodus.Tests/E2E/BugFixRegressionTests.cs` + `CustomWebApplicationFactory` + `TestHelper` kalıbında yazılacak.

1. **PR #4'ü incele ve merge et.** `dotnet build` ve `dotnet test` çalıştır. `Services/Orders/OrderService.cs`, `CampaignService.cs` ve `ExceptionHandlingMiddleware.cs` diff'lerini oku. `Baseline` migration'ının boş bir DB'ye uygulandığını doğrula. Yeşilse merge et (push/merge öncesi Oytun'un onayı alınacak).
2. **Ödeme simülasyonu:** `simulate-success` ve `simulate-fail` → `[Authorize(Roles = "Admin")]` + yalnızca Development ortamı (değilse 404). Aynı controller'da `GET intents/{id}`, `order/{orderId}` ve `events` uçlarında sahiplik kontrolü var mı bak; yoksa ekle.
3. **Eski `SellerController`'ı kaldır.** İşlevi `SellerOrderController` (`/api/seller/orders`) ve `ShipmentsController`'da zaten var. Önce `ShipmentsController`'ın `seller-orders/{id}/...` uçlarında sahiplik kontrolü olduğunu doğrula; yoksa ekle. `api-test.http` ve testlerde eski route'a referans varsa güncelle.
4. **Public listing araması:** `GET /api/listings/search` (`[AllowAnonymous]`). Mevcut `ListingSearchDto` kullanılacak. Parametreler: `q`, `categoryId`, `minPrice`, `maxPrice`, `condition`, `sortBy` (price | createdAt), `sortDesc`, `page`, `pageSize`. Yalnızca aktif listing'ler dönecek. Yanıt: `{items, totalCount, page, pageSize}`. Her item: `listingId`, `productId`, `productName`, kısa açıklama, `categoryName`, `sellerId`, `sellerName`, `price`, `stockQuantity`, `stockStatus`, `condition`. Filtre sorgusu `AdminListingController`'daki mantıktan `ListingService` içine çıkarılıp ortak kullanılacak (kopyala-yapıştır değil).
5. **Seeder:** listing'ler yalnızca `Seller` rolündeki kullanıcılara atanacak. Veri seti: ~5 kategori (Elektronik, Ev, Spor, Kitap, Moda), ~15–20 ürün (`CategoryId` atanmış). İki satıcıda örtüşen ürünler olacak ki fiyat karşılaştırması yapılabilsin; birkaç listing düşük stokta ya da tükenmiş olacak. Her müşteri için bir varsayılan adres eklenecek. Seeder idempotent kalacak.
6. **Kapıda ödeme:** `PaymentService`'te `CashOnDelivery` intent'inin kart istemeden oluşturulduğunu ve siparişi satıcının onaylayabileceği bir duruma getirdiğini doğrula. Bu, B'deki `place_order` tasarımını belirliyor. Çalışmıyorsa düzelt.
7. **Konfigürasyon ve dokümantasyon:** `appsettings.json` callback ve `BaseUrl` değerlerini düzelt. README'deki route tablolarını koda uydur; demo hesaplarını ve kurulum adımlarını (SQL Server container + user-secrets) yaz.
8. **CI:** `.github/workflows/ci.yml` dosyasını ekle; .NET 10 ile `dotnet build` + `dotnet test` çalışacak.

## Alt proje B — Exodus MCP, Aşama 1 (stdio)

### Repo yapısı (`~/Projects/Exodus MCP`)

```
Exodus.Mcp.sln
src/Exodus.Mcp.Core/          # class library — host'tan bağımsız; Aşama 2'de aynen kullanılacak
  Api/ExodusApiClient.cs      # typed HttpClient; her endpoint için tek metod
  Api/Dtos/*.cs               # Exodus yanıtları için record'lar (yalnızca kullanılan alanlar)
  Api/ExodusApiException.cs   # ProblemDetails → status + detail
  Auth/ExodusAuthHandler.cs   # DelegatingHandler: Bearer ekler, 401'de yeniler
  Auth/TokenStore.cs          # login/refresh, SemaphoreSlim ile tek seferde bir yenileme
  Tools/CustomerTools.cs      # [McpServerToolType]
  Tools/SellerTools.cs        # [McpServerToolType]
  Tools/CommonTools.cs        # whoami
  Prompts/ExodusPrompts.cs    # [McpServerPromptType]
  Resources/CatalogResources.cs  # [McpServerResourceType]
  ServiceCollectionExtensions.cs # AddExodusMcp(config): profile'a göre araçları kaydeder
src/Exodus.Mcp.Stdio/         # console: Host + AddMcpServer().WithStdioServerTransport()
tests/Exodus.Mcp.Tests/       # xUnit + FluentAssertions (Exodus ile aynı test stack)
README.md, docs/demo.gif
```

### Konfigürasyon ve kimlik doğrulama

- Ortam değişkenleri: `EXODUS_BASE_URL` (varsayılan `http://localhost:5013`), `EXODUS_PROFILE` (`customer` | `seller`), `EXODUS_USERNAME`, `EXODUS_PASSWORD`. Şifre sohbete hiç girmiyor.
- Claude'a **iki ayrı sunucu** olarak eklenecek, ikisi aynı binary: `exodus-customer` (ahmet@test.com) ve `exodus-seller` (zeynep@test.com). `EXODUS_PROFILE`'a göre yalnızca o rolün araçları kaydediliyor. Böylece her instance'ta ~10–12 araç oluyor ve LLM araç seçiminde zorlanmıyor.
- Giriş tembel (lazy) yapılıyor: ilk araç çağrısında `POST /api/auth/login`. Başlangıçta Exodus'a bağlanılmıyor, yani Exodus kapalıyken de sunucu açılıyor ve anlamlı bir hata veriyor. Login yanıtındaki `role`, `EXODUS_PROFILE` ile uyuşmazsa net bir hata dönülüyor. `twoFactorRequired=true` gelirse "2FA kapalı bir hesap kullanın" hatası veriliyor.
- Token yenileme: süre dolmasına <60 sn kaldıysa ya da 401 alındıysa `POST /api/auth/refresh` (rotating; yeni refresh token saklanıyor). Refresh başarısız olursa yeniden login deneniyor. Auth uçları 10/dk rate limit'li ve 5 hatalı denemede hesap kilitleniyor; bu yüzden döngü yok, tek deneme yapılıyor.
- `userId` (sepet uçları istiyor) login yanıtından alınıyor; LLM'e parametre olarak verilmiyor.

### Araçlar

Hepsine `Description` yazılacak ve parametrelerine `[Description]` eklenecek. Okuma araçları `ReadOnly = true`. `cancel_order` `Destructive = true`. Yazma araçlarında Claude kullanıcıdan onay ister.

**Müşteri profili (12):**

| Araç | Exodus ucu |
|---|---|
| `search_listings(query?, categoryId?, minPrice?, maxPrice?, condition?, sortBy?, page?)` | `GET /api/listings/search` (A4'te eklenecek) |
| `get_listing(listingId)` | `GET /api/listings/{id}` + `GET /api/product/{productId}` |
| `list_categories()` | `GET /api/category/tree` |
| `get_cart()` | `GET /api/cart/my` |
| `add_to_cart(listingId, quantity)` | `POST /api/cart/add` |
| `update_cart_item(cartItemId, quantity)` | `PUT /api/cart/{userId}/item/{cartItemId}` |
| `remove_from_cart(cartItemId)` | `DELETE /api/cart/{userId}/item/{cartItemId}` |
| `list_my_addresses()` | `GET /api/address` |
| `place_order(shippingAddressId, billingAddressId?, couponCode?, note?)` | `POST /api/order/checkout` + (A6 doğrulanırsa) `POST /api/payment/intents` `method=CashOnDelivery` |
| `list_my_orders(status?, page?)` | `GET /api/order` |
| `get_order(orderId? \| orderNumber?)` | `GET /api/order/{id}` veya `number/{orderNumber}` |
| `cancel_order(orderId, reason, note?)` | `POST /api/order/{id}/cancel` |

**Satıcı profili (10):**

| Araç | Exodus ucu |
|---|---|
| `list_my_listings(stockFilter: all\|low\|out, isActive?, page?)` | `GET /api/seller/listings`, `/low-stock`, `/out-of-stock` |
| `create_listing(productId? \| newProductName + description, price, stockQuantity, condition)` | Gerekirse `POST /api/product`, ardından `POST /api/seller/listings` |
| `update_listing(listingId, price?, isActive?, lowStockThreshold?)` | `PUT /api/seller/listings/{id}` |
| `update_stock(listingId, stockQuantity)` | `PATCH /api/seller/listings/{id}/stock` |
| `list_seller_orders(status?, page?)` | `GET /api/seller/orders` |
| `get_seller_order(sellerOrderId)` | `GET /api/shipments/seller-orders/{id}` + `/timeline` |
| `confirm_order(sellerOrderId)` | `POST /api/seller/orders/{id}/confirm` |
| `ship_order(sellerOrderId, carrier, trackingNumber)` | `POST /api/seller/orders/{id}/ship` |
| `mark_delivered(sellerOrderId)` | `PATCH /api/shipments/seller-orders/{id}/deliver` |
| `sales_summary(fromDate?, toDate?)` | MCP içinde hesaplanıyor: `GET /api/seller/orders` tüm sayfaları → ciro, sipariş sayısı, durum dağılımı, en çok satan listing'ler. Exodus'taki seller dashboard ucu boş bir stub |

**Ortak:** `whoami()` → `GET /api/auth/me`.

**Prompt'lar:**
- `seller_daily_report`: düşük stok + bekleyen siparişler + son 7 günün satış özeti.
- `shopping_assistant`: ara → karşılaştır → sepete ekle → adresi teyit et → **sipariş vermeden önce özet çıkarıp onay al**.

**Resource:** `exodus://categories` (kategori ağacı).

Araçlar yalnızca ihtiyaç duyulan alanları, kısa ve okunur JSON olarak dönüyor. Ham Exodus yanıtı olduğu gibi LLM'e verilmiyor.

### Hata yönetimi

- `ExodusApiClient`, başarısız yanıtlarda ProblemDetails'ı (`status`, `detail`) okuyup `ExodusApiException` fırlatıyor.
- Araç katmanında bu, `McpException` ile Türkçe ve eyleme dönük bir mesaja çevriliyor (`isError: true`). Örnekler: 404 → "Listing bulunamadı (id=…)", 403 → "Bu sipariş size ait değil", 429 → "Exodus rate limit'e takıldı, biraz bekleyin", bağlantı hatası → "Exodus'a ulaşılamıyor (EXODUS_BASE_URL=…). API çalışıyor mu?".
- Loglar yalnızca stderr'e yazılıyor; stdout MCP protokolüne ayrılmış.

### Testler

- **Unit:** sahte `HttpMessageHandler` ile `ExodusApiClient` (route'lar, query string, hata eşleme); `TokenStore` (lazy login, refresh rotasyonu, 401 → refresh → tekrar deneme, refresh başarısız → login); `sales_summary` hesaplaması; profile göre araç kaydı (customer profilinde seller araçları görünmüyor).
- **Protokol testi:** SDK'nin client API'si ile in-memory bağlantı. `ListTools` beklenen araçları ve annotation'ları döndürmeli; `CallTool` örnek çağrıları sahte Exodus yanıtlarıyla doğru metni dönmeli.
- **Uçtan uca (manuel, lokal Exodus ile):** aşağıdaki Doğrulama bölümü.

### README (portföyün asıl vitrini)

- Ne olduğu (tek paragraf) + demo GIF.
- Mimari diyagramı: Claude → MCP (stdio) → Exodus API → SQL Server.
- Araç tablosu.
- Kurulum: Exodus'u çalıştırma, `claude mcp add ...` ve Claude Desktop config örneği.
- Tasarım kararları: neden iki profil, neden kart bilgisi yok, token yönetimi.
- "MCP yazarken Exodus'ta bulup kapattığım açıklar" bölümü (Exodus PR'larına linkle).

## Aşama 2 — HTTP / deploy (yalnızca taslak; Aşama 1 bitince ayrı spec)

- `src/Exodus.Mcp.Http`: ASP.NET Core + `ModelContextProtocol.AspNetCore`, `WithHttpTransport(o => o.Stateless = true)` + `MapMcp()`. `Exodus.Mcp.Core` hiç değişmiyor.
- Kimlik: uzak sunucuda ortak env şifresi kullanılamaz. Her MCP istemcisi kendi Exodus JWT'sini `Authorization: Bearer` header'ıyla gönderiyor, sunucu bunu Exodus'a aktarıyor (pass-through). Tam OAuth 2.1 kapsam dışı.
- Exodus + SQL Server + MCP için `docker-compose`, hosting seçimi, `AllowedHosts` kısıtı ve demo hesap stratejisi o spec'te kararlaştırılacak.

---

## Doğrulama

**A (Exodus):**
1. `dotnet test` tamamen yeşil olmalı; her düzeltme için yeni regresyon testleri var (simulate yetkisi, eski seller uçlarının kalkması ve shipments sahiplik kontrolü, search filtreleri ve sayfalama, seeder'da listing'lerin yalnızca satıcılara ait olması).
2. Container'da SQL Server ayağa kaldırılıp `dotnet run` yapılacak. Ardından `curl` ile: `zeynep@test.com / Demo123!` girişi, `GET /api/listings/search?q=…&maxPrice=…&sortBy=price`, müşteri token'ıyla `simulate-success` → 403/404.
3. CI workflow'u GitHub'da yeşil olmalı.

**B (MCP):**
1. `dotnet test` yeşil olmalı.
2. MCP Inspector ile kontrol: `npx @modelcontextprotocol/inspector dotnet run --project src/Exodus.Mcp.Stdio`. Her iki profilde araç listesi, annotation'lar ve örnek çağrılar doğru olmalı.
3. Claude Code'a eklenecek: `claude mcp add exodus-customer -e EXODUS_PROFILE=customer -e EXODUS_USERNAME=ahmet@test.com -e EXODUS_PASSWORD=Demo123! -- dotnet run --project <yol>/src/Exodus.Mcp.Stdio` (seller için aynısı zeynep@ ile).
4. **Uçtan uca senaryo:**
   - Müşteri: "1500 TL altı elektronik ürünlerden en ucuzunu sepete ekle ve kayıtlı adresime sipariş ver" → sipariş oluşur.
   - Satıcı: "Bekleyen siparişlerim neler? Onayla ve Yurtiçi ile TR123 takip numarasıyla kargoya ver" → durum Shipped olur.
   - Müşteri: "Siparişim ne durumda?" → Shipped görünür.
   - Satıcı: "Stoğu azalan ürünlerime 20 adet ekle", ardından `seller_daily_report` prompt'u.
   - Bu akış README için GIF olarak kaydedilecek.
5. Hata yolları: Exodus kapalıyken anlamlı bir hata mesajı gelmeli; yanlış şifrede hesap kilitlenmeden tek ve net bir hata dönmeli; customer profilinde seller araçları görünmemeli.

## Süreç notları

- Bu plan onaylanınca spec, MCP reposuna `docs/superpowers/specs/2026-11-18-exodus-mcp-design.md` olarak kopyalanacak (repo `git init` ile açılacak). Ardından önce **A** için ayrıntılı uygulama planı (writing-plans), A bitince **B** için ayrı plan yazılacak.
- Yazılım kurulumu (dotnet, OrbStack), Exodus'ta merge ve push, GitHub'a yeni repo açma gibi geri alınması zor adımlarda Oytun'un onayı ayrıca alınacak.
