# Exodus MCP — Aşama 1 (stdio): Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exodus API'sini müşteri ve satıcı olarak kullanan bir stdio MCP sunucusu kurmak. Çıktı: test edilmiş bir .NET 10 çözümü, Claude Code'a eklenebilir binary, README.

**Architecture:**
- **Katmanlar:** `Exodus.Mcp.Core` host'tan bağımsız bir class library. İçinde typed `ExodusApiClient`, `TokenStore` + `ExodusAuthHandler`, araç, prompt ve resource sınıfları ve `AddExodusMcp` bulunuyor. `Exodus.Mcp.Stdio` ise ince bir console host.
- **Profil:** `EXODUS_PROFILE` hangi araç sınıflarının kaydedileceğini belirliyor.
- **Araç çıktısı ve hatalar:** Araçlar Exodus yanıtını kısa view JSON'una çeviriyor. Exodus hataları Türkçe `McpException` mesajlarına dönüşüyor.
- **Exodus tarafı:** MCP'nin ihtiyaç duyduğu iki küçük ek önce Exodus'ta yapılıyor (Task 1–2).

**Tech Stack:** .NET 10 (`net10.0`), `ModelContextProtocol` 2.4.0, `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Http` 10.0.15. Test tarafı Exodus ile aynı: xUnit 2.9.3, FluentAssertions 8.0.1.

**Spec:** `/Users/oytunemreozmel/Projects/Exodus MCP/docs/superpowers/specs/2026-11-18-exodus-mcp-design.md` ("Alt proje B" ve "Doğrulama B" bölümleri).
- Task 1–2 şu repoda uygulanıyor: `/Users/oytunemreozmel/Projects/Exodus`.
- Task 3–13 şu repoda uygulanıyor: `/Users/oytunemreozmel/Projects/Exodus MCP`.

**Kaynak notları:** Exodus uçlarının gerçek şekilleri ve MCP C# SDK 2.4.0 API'si 2026-11-18'de kaynaktan çıkarıldı. Aşağıdaki kararlar bu bilgilere dayanıyor.

## Spec'ten sapmalar ve kararlar

1. **Exodus'a iki ek yapılıyor.**
   - Task 1: Satıcı sipariş listesinde `orderNumber`, `createdAt`, `orderId`, kalem `listingId` ve dolu `shipment` alanları yok. Tek bir satıcı siparişini getiren uç da yok.
   - Task 2: `POST /api/seller/listings` boş gövde dönüyor ve fiyat/stok doğrulaması yapmıyor.
   - Karşılığında MCP'deki `get_seller_order`, shipment ucu yerine yeni `GET /api/seller/orders/{id}` ucunu kullanıyor. Shipment ucu kargodan önce 404 dönüyor. Timeline yalnızca shipment varsa çekiliyor.
2. **`place_order`'da `couponCode` yok.** Exodus checkout bu alanı okumuyor; araç kupon uygulanmış gibi davranmamalı.
3. **`search_listings` sıralama parametreleri:** `sortBy` değerleri `price|newest|name`. Ek olarak `sortOrder` (`asc|desc`) parametresi var.
4. **`create_listing` için barkod gerekiyor.** Yeni ürün açarken `newProductBarcode` zorunlu, çünkü Exodus ürün başına en az bir barkod istiyor.
5. **Prompt'lar profile göre kaydediliyor.** `CustomerPrompts.shopping_assistant` ve `SellerPrompts.seller_daily_report` aynı dosyada iki ayrı sınıf.
6. **Login hatası kilitleniyor.** Login 401, 2FA ya da rol uyuşmazlığı verirse `TokenStore` hatayı süreç ömrü boyunca hatırlıyor ve bir daha login denemiyor. Böylece 5 hatalı denemede hesap kilitlenmiyor (spec Doğrulama B.5). Ağ hataları ve 429 kilitlenmiyor; sonraki çağrı tekrar deniyor.
7. **Konfigürasyon başlangıçta doğrulanıyor.** Ağa çıkılmadan kontrol ediliyor. Hata varsa stderr'e mesaj yazılıyor ve exit code 2 dönüyor, çünkü profil olmadan hangi araçların kaydedileceği bilinemiyor.
8. **Araçlar `string` döndürüyor.** Dönüş değeri `ToolJson` ile üretilen kısa JSON. Kodlama: camelCase, null'lar atlanıyor, Türkçe karakterler kaçırılmıyor.
9. **`sales_summary` hesaplama kuralları:**
   - Varsayılan aralık son 30 UTC günü. Tarihler `yyyy-MM-dd` biçiminde ve sınırlar dahil.
   - Exodus zaman damgaları UTC kabul ediliyor; SQL Server'dan `Z` olmadan geliyorlar.
   - En çok satanlar `listingId` bazında hesaplanıyor.
10. **Dil:** README ve kod İngilizce. Araç açıklamaları ve araçların döndürdüğü hata mesajları Türkçe.
11. **Dallar:**
    - Exodus: `feat/seller-api-for-mcp`, `main`'den açılıyor.
    - MCP repo: `feat/phase1-stdio`, `main`'den açılıyor.
    - Merge ve push için kullanıcı onayı gerekiyor.

## Global Constraints

- **Ortam:** `export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_UI_LANGUAGE=en`. SDK 10.0.404. Makinenin kültürü `tr-TR`.
- **SDK sabitleme:** MCP reposunda Exodus'takiyle aynı `global.json` olacak: `{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false}}`.
- **Paketler:** sürümler tam olarak sabit. Listede olmayan bir paket eklemek için ruling gerekiyor.
  - Core: `ModelContextProtocol` 2.4.0, `Microsoft.Extensions.Http` 10.0.15.
  - Stdio: `Microsoft.Extensions.Hosting` 10.0.15.
  - Test: `Microsoft.NET.Test.Sdk` 17.12.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `coverlet.collector` 6.0.2, `FluentAssertions` 8.0.1.
- **`Directory.Build.props`:** `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`.
- **stdout yalnızca MCP protokolüne ait.** `Console.Write*` yasak. Loglar stderr'e gidiyor.
- **Kart bilgisi:** MCP hiçbir parametrede ve çıktıda kart bilgisi taşımıyor. Ödeme yalnızca `CashOnDelivery` intent'i.
- **Gizli bilgiler:** şifre, access token ve refresh token hiçbir araç çıktısında, logda, exception mesajında ya da `ToString()` çıktısında görünmüyor.
- **Ortam değişkenleri (adlar tam olarak bunlar):**
  - `EXODUS_BASE_URL`, varsayılanı `http://localhost:5013`.
  - `EXODUS_PROFILE`: `customer` ya da `seller`, büyük/küçük harf duyarsız.
  - `EXODUS_USERNAME`.
  - `EXODUS_PASSWORD`.
- **Araç adları:** spec'teki adlar, `[McpServerTool(Name = "...")]` ile açıkça veriliyor.
- **Araç annotation'ları:** her araçta `ReadOnly`, `Destructive`, `Idempotent` ve `OpenWorld` açıkça set ediliyor; SDK yalnızca set edilen hint'leri yayınlıyor. Değerler Task 6'daki tabloda. `OpenWorld` her araçta `false`.
- **İstek biçimi:**
  - Exodus enum'ları her iki yönde string olarak taşınıyor; DTO'larda tipleri `string`.
  - Query string'deki sayılar `CultureInfo.InvariantCulture` ile yazılıyor.
  - İstekler başında `/` olmayan relative yol kullanıyor (`api/...`); `BaseAddress` her zaman `/` ile bitiyor.
- **Hata mesajları:** araç tarafındaki mesajlar yalnızca `McpException` ile çıkıyor. SDK diğer exception'ların mesajını gizliyor. `McpProtocolException` kullanılmıyor.
- **Commit:** her commit mesajı şu iki satırla bitiyor:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DUdemcEHhejupHBqj7mmVp
  ```
- **Test:** her task sonunda ilgili repoda tüm testler yeşil.
  - Exodus: `dotnet test Exodus.sln`; başlangıçta 504 test var.
  - MCP: `dotnet test Exodus.Mcp.sln`.

## Review Focus

1. **Kültür:** Makine `tr-TR`. `minPrice=1500.5` query'de `1500,5` olarak gitmemeli. Test: Task 7 `SearchListings_FormatsDecimalsInvariant_UnderTurkishCulture`.
2. **Yanlış şifre:** Arka arkaya araç çağrıları hesabı kilitlememeli; tek login isteği gitmeli, sonraki çağrılar aynı net hatayı almalı. Test: Task 5 `BadPassword_IsLatched_SingleLoginRequest`.
3. **Exodus kapalı:** Sunucu yine açılmalı ve araç listesi gelmeli. Araç çağrısı "Exodus'a ulaşılamıyor (EXODUS_BASE_URL=…)" demeli. Test: Task 6 `ConnectionRefused_MapsToUnreachableMessage`, Task 12 `StdioHost_StartsWithoutExodus_AndReportsUnreachable`.
4. **LLM'in yazdığı enum'lar:** `"likenew"`, `"cashondelivery"`, `"PENDING"` gibi değerler Exodus'un kanonik adına normalize edilmeli. Geçersiz değer izin verilen değerleri listeleyen bir hata vermeli. Test: Task 6 `ToolInput_OneOf_*`, Task 7 `SearchListings_NormalizesCondition`.
5. **ProblemDetails olmayan hata gövdeleri:** Shipments'ın döndüğü `{message}`, gövdesi boş 429 ve `errors` sözlüklü ValidationProblemDetails okunur bir Türkçe mesaja dönmeli. Test: Task 4 `ParsesMessageBody` / `EmptyBody429` / `ParsesValidationErrors`, Task 10 `MarkDelivered_BeforeShip_ShowsExodusMessage`.

---

### Task 1 (Exodus): Satıcı sipariş listesini zenginleştir + tekil satıcı siparişi ucu

**Files:**
- Create: `Models/Dto/OrderDto/SellerOrderListItemDto.cs`. İçinde `SellerOrderListItemDto` ve `SellerOrderLineDto` var.
- Modify: `Services/Orders/IOrderService.cs`, `Services/Orders/OrderService.cs` (`GetSellerOrdersAsync`, yaklaşık :485–521), `Controllers/Seller/SellerOrderController.cs`.
- Test: `Exodus.Tests/E2E/SellerOrderReadTests.cs`. Senaryo kurulumu için `Exodus.Tests/E2E/ShipmentOwnershipTests.cs`'teki `CreateListingAsync` / `CreateScenarioAsync` kalıbını kullan.

**Interfaces:**
- Produces (JSON, `GET /api/seller/orders` ve yeni `GET /api/seller/orders/{sellerOrderId:int}`):
  ```
  { id, orderId, orderNumber, createdAt, sellerId, sellerName, status (OrderStatus string), total,
    items: [{ id, listingId, productId, productName, quantity, unitPrice, totalPrice }],
    shipment: { id, carrier, trackingNumber, shippedAt, deliveredAt } | null }
  ```
- Kurallar:
  - Liste yine çıplak dizi dönüyor. Mevcut alanların adı ve anlamı değişmiyor (eski istemciler için üst küme).
  - `createdAt` alanı `SellerOrder.CreatedAt` değeri.
  - Müşteri tarafındaki `OrderDetailResponseDto.sellerOrders` değişmiyor.
- `IOrderService`: `Task<List<SellerOrderListItemDto>> GetSellerOrdersAsync(int sellerId, OrderStatus? status = null, int page = 1, int pageSize = 20)` (dönüş tipi değişiyor) ve yeni `Task<SellerOrderListItemDto> GetSellerOrderAsync(int sellerId, int sellerOrderId)`. Kayıt bulunamazsa ya da başka satıcıya aitse `NotFoundException("Satıcı siparişi bulunamadı.")` fırlatılıyor; bu, mevcut confirm davranışıyla aynı.
- Mapping liste ve tekil uç için tek bir yerde yapılıyor (private static mapper).

- [ ] **Step 1: Branch:** `git switch main && git pull --ff-only && git switch -c feat/seller-api-for-mcp`.
- [ ] **Step 2: Failing testler.** Hepsi `JsonElement` üzerinden alan kontrol ediyor.
  - `GetSellerOrders_IncludesOrderNumberCreatedAtOrderIdAndListingIds`: checkout sonrası satıcı listesindeki kayıtta `orderId == order.Id` ve `orderNumber == order.OrderNumber`. `createdAt`, şu andan en fazla 2 dakika uzakta. `items[0].listingId` oluşturulan listing'in id'si. `status == "Pending"`.
  - `GetSellerOrders_ShipmentPopulatedAfterShip`: `POST /api/seller/orders/{id}/ship {carrier:"Yurtici", trackingNumber:"TR123"}` sonrası `shipment.carrier == "Yurtici"` ve `shipment.trackingNumber == "TR123"`. Kargodan önce `shipment` null.
  - `GetSellerOrderById_Owner_ReturnsSameShapeAsList`: 200 dönüyor. `orderNumber` ve `items[0].listingId` dolu.
  - `GetSellerOrderById_OtherSeller_Returns404`.
  - `GetSellerOrderById_Customer_Returns403`: `SellerOnly` policy'si.
- [ ] **Step 3:** `dotnet test Exodus.sln --filter FullyQualifiedName~SellerOrderReadTests` çalıştır. Testler başarısız olmalı: alan eksik ya da route 404.
- [ ] **Step 4: Uygula.** DTO'ları ve mapper'ı yaz. `GetSellerOrderAsync`'i ekle. Controller'a `[HttpGet("{sellerOrderId:int}")]` ucunu ekle; caller id'si mevcut kalıpla alınıyor. Query'ye `.Include(so => so.Order)` zaten var; `Shipment` alanını map'le.
- [ ] **Step 5: Doğrula.**
  - `dotnet build Exodus.sln -warnaserror:NU1902,NU1903,NU1904` → 0 uyarı.
  - `dotnet test Exodus.sln` → tüm testler geçiyor.
- [ ] **Step 6:** Commit `feat(seller-orders): expose order number, dates, listing ids and a single seller-order endpoint`.

### Task 2 (Exodus): Satıcı listing yazma uçlarında doğrulama + oluşturulan listing'i döndür

**Files:**
- Modify: `Controllers/Seller/SellerListingController.cs`. Etkilenen yerler: `CreateListing` (:100–135), `UpdateListing` (:140–176), `UpdateStock` (:181–196) ve `GetMyListings` projeksiyonu.
- Create: `Models/Dto/SellerDto/SellerListingItemDto.cs`. Mevcut liste item'ının alanlarını taşıyor: `id, productId, productName, primaryImage, price, stockQuantity, stockStatus, lowStockThreshold, sku, condition, isActive, createdAt, updatedAt`. JSON adları değişmiyor.
- Test: `Exodus.Tests/E2E/SellerListingWriteTests.cs`.

**Interfaces:**
- `POST /api/seller/listings` → **201 + `SellerListingItemDto` gövdesi**. Location header mevcut haliyle kalıyor.
- Doğrulama hepsi `BadRequestException` ile, gelen DTO'da alan doluysa uygulanıyor:
  - `price <= 0` → "Price must be greater than 0."
  - `stockQuantity < 0` → "Stock quantity cannot be negative."
  - `lowStockThreshold < 0` → "Low stock threshold cannot be negative."
  - Kural POST, PUT ve PATCH stock uçlarının hepsinde aynı.
- `GET /api/seller/listings` item'ları `SellerListingItemDto` projeksiyonunu kullanıyor; JSON aynı kalıyor.

- [ ] **Step 1: Failing testler**
  - `CreateListing_ReturnsCreatedListingBody`: 201 dönüyor. Gövdede `id > 0`, `price == 199.90m`, `stockQuantity == 5`, `stockStatus == "InStock"`, `condition == "New"`, `isActive == true`. Aynı `id`, `GET /api/seller/listings` sonucunda da görünüyor.
  - `CreateListing_NonPositivePrice_Returns400`: `[Theory]` ile `0` ve `-5` değerleri.
  - `CreateListing_NegativeStock_Returns400`.
  - `UpdateListing_NegativePrice_Returns400_AndPriceUnchanged`.
  - `UpdateListing_NegativeLowStockThreshold_Returns400`.
  - `UpdateStock_Negative_Returns400_AndStockUnchanged`.
- [ ] **Step 2:** `dotnet test Exodus.sln --filter FullyQualifiedName~SellerListingWriteTests` çalıştır ve FAIL gör.
- [ ] **Step 3: Uygula.** Doğrulamayı tek bir private helper'da yaz, üç uç da onu çağırsın. `CreatedAtAction(..., body)` kullan. README'deki seller listing satırında POST yanıtı artık "returns the created listing" diyor; bunu güncelle.
- [ ] **Step 4: Doğrula.** Task 1 Step 5'teki iki komutu çalıştır.
- [ ] **Step 5:** Commit `fix(seller-listings): validate price/stock and return the created listing`.

### Task 3 (MCP): Çözüm iskeleti, konfigürasyon ve CI

**Files:**
- Create:
  - Kök dosyalar: `global.json`, `Directory.Build.props`, `Exodus.Mcp.sln`, `.github/workflows/ci.yml`.
  - Core: `src/Exodus.Mcp.Core/Exodus.Mcp.Core.csproj`, `src/Exodus.Mcp.Core/Configuration/ExodusOptions.cs`, `src/Exodus.Mcp.Core/Configuration/ExodusProfile.cs`, `src/Exodus.Mcp.Core/Configuration/ExodusConfigurationException.cs`.
  - Test: `tests/Exodus.Mcp.Tests/Exodus.Mcp.Tests.csproj`, `tests/Exodus.Mcp.Tests/Configuration/ExodusOptionsTests.cs`.
- Modify: `.gitignore`; mevcut `.remember/` satırına `bin/`, `obj/`, `*.user` ekleniyor.

**Interfaces:**
- Produces:
  ```csharp
  public enum ExodusProfile { Customer, Seller }
  public sealed class ExodusOptions   // record değil: otomatik ToString şifreyi basmasın
  {
      public const string DefaultBaseUrl = "http://localhost:5013";
      public required Uri BaseUrl { get; init; }        // her zaman '/' ile biter
      public required ExodusProfile Profile { get; init; }
      public required string Username { get; init; }
      public required string Password { get; init; }
      public static ExodusOptions FromConfiguration(IConfiguration configuration); // anahtarlar: EXODUS_BASE_URL, EXODUS_PROFILE, EXODUS_USERNAME, EXODUS_PASSWORD
      public override string ToString();                // "Profile=Customer, BaseUrl=..., Username=..." — şifre YOK
  }
  public sealed class ExodusConfigurationException(string message) : Exception(message);
  ```
- Hata metinleri:
  - `FromConfiguration` tüm sorunları satır satır birleştirip tek bir `ExodusConfigurationException` fırlatıyor.
  - Profil: `EXODUS_PROFILE 'customer' veya 'seller' olmalı (gelen: '<değer>').`
  - Kullanıcı adı: `EXODUS_USERNAME tanımlı değil.`
  - Şifre: `EXODUS_PASSWORD tanımlı değil.`
  - Adres: `EXODUS_BASE_URL geçerli bir http(s) adresi olmalı (gelen: '<değer>').`

- [ ] **Step 1: Branch:** `cd "/Users/oytunemreozmel/Projects/Exodus MCP" && git switch -c feat/phase1-stdio`.
- [ ] **Step 2: Proje dosyaları.**
  - `dotnet new sln -n Exodus.Mcp`, `dotnet new classlib`, `dotnet new xunit` ile oluştur.
  - Paket sürümlerini Global Constraints'teki gibi sabitle.
  - Test projesi Core'a referans veriyor.
  - Şablonun ürettiği `Class1.cs` ve `UnitTest1.cs` dosyalarını sil.
- [ ] **Step 3: Failing testler** (`ExodusOptionsTests`, `ConfigurationBuilder().AddInMemoryCollection(...)` ile)
  - `Parses_AllValues_ProfileCaseInsensitive`: `"SELLER"` → `Seller`. `BaseUrl == http://h:1/`.
  - `BaseUrl_DefaultsToLocalhost5013_AndAlwaysEndsWithSlash`: boş → `http://localhost:5013/`. `http://h:1/x` → `http://h:1/x/`.
  - `ReportsEveryProblemAtOnce`: hiçbir değer verilmezse mesajda dört satırın üçü bulunuyor (profil, kullanıcı adı, şifre). Base URL varsayılana düşüyor.
  - `RejectsNonHttpBaseUrl`: `ftp://x` değeri reddediliyor.
  - `ToString_NeverContainsPassword`: şifre `"S3cret!"` iken `ToString()` çıktısı bu değeri içermiyor.
- [ ] **Step 4:** `dotnet test Exodus.Mcp.sln` çalıştır; testler FAIL vermeli (tip yok).
- [ ] **Step 5: Uygula.** Config değerleri `Trim()` ediliyor, boş string "tanımlı değil" sayılıyor.
- [ ] **Step 6: CI dosyası.** `ci.yml` Exodus'taki CI'ın aynısı. Tek fark: build ve test hedefi `Exodus.Mcp.sln`.
  - Build: `dotnet build Exodus.Mcp.sln -warnaserror:NU1902,NU1903,NU1904`.
  - Test: `dotnet test Exodus.Mcp.sln --no-build`.
  - `permissions: contents: read`.
- [ ] **Step 7: Doğrula.** `dotnet build Exodus.Mcp.sln -warnaserror:NU1902,NU1903,NU1904` → 0 uyarı. Ardından `dotnet test Exodus.Mcp.sln` → hepsi geçiyor.
- [ ] **Step 8:** Commit `chore: scaffold solution, configuration and CI`.

### Task 4 (MCP): ExodusApiClient çekirdeği ve hata ayrıştırma

**Files:**
- Create: `src/Exodus.Mcp.Core/Api/ExodusApiClient.cs`, `src/Exodus.Mcp.Core/Api/ExodusApiException.cs`, `src/Exodus.Mcp.Core/Api/QueryString.cs`.
- Test altyapısı: `tests/Exodus.Mcp.Tests/Fakes/FakeExodusHandler.cs`, `tests/Exodus.Mcp.Tests/Fakes/ManualTimeProvider.cs`.
- Test: `tests/Exodus.Mcp.Tests/Api/ExodusApiClientCoreTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public sealed partial class ExodusApiClient(HttpClient http)
  {
      internal Task<T> SendAsync<T>(HttpMethod method, string relativePath, object? jsonBody, CancellationToken ct);
      internal Task SendAsync(HttpMethod method, string relativePath, object? jsonBody, CancellationToken ct); // gövdeyi okumaz
  }
  public sealed class ExodusApiException : Exception
  {
      public int StatusCode { get; }
      public string? Detail { get; }      // insan-okunur özet (aşağıdaki öncelikle)
      public static Task<ExodusApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct);
  }
  public static class QueryString
  {
      public static string Build(string relativePath, params (string Key, object? Value)[] parameters); // null'ları atlar, InvariantCulture, Uri.EscapeDataString
  }
  ```
- `ExodusApiClient` davranışı:
  - JSON ayarları: web varsayılanları (camelCase, büyük/küçük harf duyarsız okuma).
  - Gövde `JsonContent.Create` ile oluşturuluyor.
  - Başarılı yanıtta gövde boşsa ve `T` nullable değilse `InvalidOperationException` fırlatılıyor.
- `Detail` öncelik sırası:
  1. ProblemDetails `detail`
  2. `{ "message": ... }`
  3. ValidationProblemDetails `errors`; `"alan: mesaj; alan2: mesaj"` biçiminde birleştiriliyor.
  4. `title`
  5. null (boş gövde ya da JSON olmayan gövde)
- Test altyapısı (sonraki tüm task'lar kullanıyor):
  ```csharp
  public sealed class FakeExodusHandler : HttpMessageHandler
  {
      // Aynı (method, pathAndQuery) için birden çok On(...) çağrısı sıraya girer; son yanıt tekrar eder.
      public FakeExodusHandler On(HttpMethod method, string pathAndQuery, HttpStatusCode status, object? json = null, TimeSpan? delay = null);
      public FakeExodusHandler OnRaw(HttpMethod method, string pathAndQuery, HttpStatusCode status, string? body, string contentType = "application/json");
      public FakeExodusHandler WithLogin(string role = "Customer", int userId = 7, string token = "access-1", DateTime? expiresAtUtc = null, string refreshToken = "refresh-1");
      public IReadOnlyList<RecordedRequest> Requests { get; }  // Method, PathAndQuery, Authorization (string?), Body (string?)
      public int Count(HttpMethod method, string pathAndQuery);
  }
  // Eşleşmeyen istek: 501 + {"detail":"No fake for <METHOD> <pathAndQuery>"} — test bu metinle düşer.
  public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider { public void Advance(TimeSpan by); public override DateTimeOffset GetUtcNow(); }
  ```
  `WithLogin`, `POST /api/auth/login` için Exodus login yanıtını üretiyor: `{userId, name, email, username, role, token, expiresAt, refreshToken, refreshTokenExpiresAt, twoFactorRequired:false}`. `expiresAt` varsayılan olarak +24 saat.

- [ ] **Step 1: Failing testler**
  - `ParsesProblemDetailsDetail`: 404 ve `{"status":404,"title":"Request failed","detail":"Listing not found."}` → `StatusCode 404`, `Detail "Listing not found."`.
  - `ParsesMessageBody`: 400 ve `{"message":"Shipment yok. Önce ship yap."}` → mesaj olduğu gibi.
  - `ParsesValidationErrors`: 400 ve `{"title":"One or more validation errors occurred.","errors":{"Quantity":["must be > 0"]}}` → `Detail` içinde `Quantity: must be > 0` var.
  - `EmptyBody429`: 429, gövde yok → `StatusCode 429`, `Detail null`.
  - `NonJsonBody`: `text/html` → `Detail null`, exception yok.
  - `QueryString_SkipsNullsAndEscapesUtf8`: `kulaklık` → `search=kulakl%C4%B1k`. `null` değerler atlanıyor.
  - `QueryString_UsesInvariantCulture`: `CultureInfo.CurrentCulture = tr-TR` iken `1500.5m` → `1500.5`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(api): Exodus HTTP client core with ProblemDetails parsing`.

### Task 5 (MCP): TokenStore + ExodusAuthHandler

**Files:**
- Create:
  - `src/Exodus.Mcp.Core/Auth/TokenStore.cs`, `src/Exodus.Mcp.Core/Auth/ExodusAuthHandler.cs`, `src/Exodus.Mcp.Core/Auth/AuthSession.cs`, `src/Exodus.Mcp.Core/Auth/ExodusAuthException.cs`.
  - `src/Exodus.Mcp.Core/Api/Dtos/AuthDtos.cs`. Login yanıtı `token`/`expiresAt` alanlarını, refresh yanıtı `accessToken`/`accessTokenExpiresAt` alanlarını kullanıyor; ikisi farklı şekiller.
  - `src/Exodus.Mcp.Core/ExodusHttpServiceCollectionExtensions.cs`.
- Modify: `tests/Exodus.Mcp.Tests/Fakes/FakeExodusHandler.cs`; `OnThrow(HttpMethod method, string pathAndQuery, Exception error)` ekleniyor. Bir kez fırlatıyor, sonra sıradaki yanıta geçiyor.
- Test: `tests/Exodus.Mcp.Tests/Auth/TokenStoreTests.cs`, `tests/Exodus.Mcp.Tests/Auth/ExodusAuthHandlerTests.cs`.

**Interfaces:**
- Consumes: `ExodusOptions` (Task 3), `ExodusApiException.FromResponseAsync` (Task 4).
- Produces:
  ```csharp
  public sealed record AuthSession(int UserId, string Role, string AccessToken, DateTime AccessTokenExpiresAtUtc, string? RefreshToken);
  public sealed class ExodusAuthException(string message, bool permanent) : Exception(message) { public bool Permanent { get; } }
  public sealed class TokenStore(IHttpClientFactory httpClientFactory, ExodusOptions options, TimeProvider time, ILogger<TokenStore> logger)
  {
      public const string AuthClientName = "exodus-auth";
      public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
      public Task<AuthSession> GetSessionAsync(CancellationToken ct);
      public Task<AuthSession> RenewAfterUnauthorizedAsync(string rejectedAccessToken, CancellationToken ct);
  }
  public sealed class ExodusAuthHandler(TokenStore tokens) : DelegatingHandler;
  public static class ExodusHttpServiceCollectionExtensions
  {
      // options singleton, TryAdd TimeProvider.System, TokenStore singleton, "exodus-auth" named client,
      // typed ExodusApiClient (+ ExodusAuthHandler), BaseAddress = options.BaseUrl, Timeout = 30 s
      public static IServiceCollection AddExodusApi(this IServiceCollection services, ExodusOptions options);
  }
  ```
- `GetSessionAsync` kuralları (tek `SemaphoreSlim(1,1)`):
  - **Kilitli hata:** daha önce kalıcı bir hata kilitlendiyse aynı `ExodusAuthException` HTTP isteği atılmadan fırlatılıyor.
  - **Oturum yoksa:** login yapılıyor (`POST api/auth/login`, gövde `{emailOrUsername, password}`).
  - **Oturum varsa:** süre bitimine `RefreshMargin`'den az kaldıysa refresh deneniyor (`POST api/auth/refresh {refreshToken}`), yeni refresh token saklanıyor. Refresh 401 dönerse tam olarak bir login deneniyor.
  - **Login sonuç eşlemesi:**

    | Sonuç | Davranış |
    |---|---|
    | 401 | kalıcı: `Exodus girişi başarısız: <detail>. EXODUS_USERNAME/EXODUS_PASSWORD değerlerini düzeltip MCP sunucusunu yeniden başlatın.` |
    | `twoFactorRequired=true` | kalıcı: `Bu hesapta iki adımlı doğrulama açık. MCP için 2FA kapalı bir hesap kullanın.` |
    | `role` profil ile uyuşmuyor | kalıcı: `EXODUS_PROFILE=<profile> ama hesabın rolü <role>. Doğru hesabı ya da profili kullanın.` |
    | 429 | kalıcı değil: `Exodus giriş rate limit'ine takıldı (dakikada 10 istek). Bir dakika bekleyip tekrar deneyin.` |
    | bağlantı hatası | `HttpRequestException` aynen yükseliyor, kilitlenmiyor |

  - Başarılı login `Information` seviyesinde loglanıyor: kullanıcı adı ve rol yazılıyor, token yazılmıyor.
- `RenewAfterUnauthorizedAsync` kuralları:
  - Mevcut token `rejectedAccessToken`'dan farklıysa başka bir çağıran zaten yenilemiş demektir; mevcut oturum dönüyor.
  - Değilse refresh deneniyor; o da 401 verirse login deneniyor.
- `ExodusAuthHandler` kuralları:
  - Her isteğe `Authorization: Bearer <token>` ekliyor.
  - Yanıt 401 ise oturumu yenileyip aynı isteği **bir kez** yeniden gönderiyor. İkinci 401 olduğu gibi dönüyor; döngü yok.

- [ ] **Step 1: Failing testler**
  - **TokenStore** (`ManualTimeProvider` ve `FakeExodusHandler` ile, `IHttpClientFactory` gerçek DI'dan alınıyor):
    - `LazyLogin_NoRequestUntilFirstUse`.
    - `ConcurrentCallers_SingleLogin`: `delay: 50ms`, 5 paralel çağrı → `Count(POST, "/api/auth/login") == 1`.
    - `RefreshesWithin60Seconds_AndStoresRotatedRefreshToken`: saat ilerletilince `refresh` isteğinin gövdesinde `refresh-1` gidiyor. Bir sonraki refresh `refresh-2` gönderiyor.
    - `RefreshUnauthorized_FallsBackToSingleLogin`.
    - `BadPassword_IsLatched_SingleLoginRequest`: login 401 ve `{"detail":"Invalid credentials"}` → 3 çağrının üçü de aynı mesajı alıyor, `Count(login) == 1`.
    - `TwoFactorRequired_IsLatched`.
    - `RoleMismatch_IsLatched`: profil `Seller`, rol `Customer`.
    - `RateLimited429_IsNotLatched`: ilk login 429, ikinci login 200.
    - `ConnectionRefused_IsNotLatched`: fake bir kez `HttpRequestException` fırlatıyor, sonra 200. Bunun için fake'e `OnThrow(method, path, Exception)` ekle.
  - **Handler:**
    - `AddsBearerHeader`.
    - `Unauthorized_RenewsAndRetriesOnce_WithSameBody`: ilk `POST /api/cart/add` 401, ikincisi 200. İkinci isteğin `Authorization` header'ı yeni token ve `Body` birinciyle aynı.
    - `SecondUnauthorized_IsReturned_NoLoop`: istek sayısı 2.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(auth): lazy login, rotating refresh and single retry on 401`.

### Task 6 (MCP): Araç altyapısı, `whoami`, `AddExodusMcp` ve protokol test host'u

**Files:**
- Create:
  - Araç altyapısı: `src/Exodus.Mcp.Core/Tools/ToolJson.cs`, `src/Exodus.Mcp.Core/Tools/ToolErrors.cs`, `src/Exodus.Mcp.Core/Tools/ToolInput.cs`, `src/Exodus.Mcp.Core/Tools/CommonTools.cs`.
  - API: `src/Exodus.Mcp.Core/Api/ExodusApiClient.Account.cs`, `src/Exodus.Mcp.Core/ServiceCollectionExtensions.cs`.
- Modify: `src/Exodus.Mcp.Core/Api/Dtos/AuthDtos.cs`; `MeDto(string UserId, string? Username, string? Email, string? Role)` ekleniyor. Exodus `userId`'yi string döndürüyor.
  - Test altyapısı: `tests/Exodus.Mcp.Tests/Infrastructure/McpTestHost.cs`.
- Test: `tests/Exodus.Mcp.Tests/Tools/ToolInputTests.cs`, `tests/Exodus.Mcp.Tests/Tools/ToolErrorsTests.cs`, `tests/Exodus.Mcp.Tests/Protocol/CommonToolsProtocolTests.cs`.

**Interfaces:**
- Consumes: Task 3–5.
- Produces:
  ```csharp
  public static class ToolJson
  {
      public static readonly JsonSerializerOptions Options; // camelCase, WhenWritingNull, JavaScriptEncoder.UnsafeRelaxedJsonEscaping, indent yok
      public static string Serialize<T>(T value);
  }
  public static class ToolErrors
  {
      // action'ı çalıştırır, sonucu ToolJson ile string'e çevirir; hataları McpException'a çevirir.
      public static Task<string> RunAsync<T>(ExodusOptions options, Func<Task<T>> action, string? notFound = null, string? forbidden = null);
      public static Exception Translate(Exception error, ExodusOptions options, string? notFound = null, string? forbidden = null);
  }
  public static class ToolInput
  {
      // Büyük/küçük harf duyarsız eşleşme → kanonik değer; null → defaultValue; geçersiz → McpException
      public static string? OneOf(string? value, string parameterName, IReadOnlyList<string> allowed, string? defaultValue = null);
      public static DateOnly Date(string value, string parameterName); // yalnız yyyy-MM-dd
      public static void Require(bool condition, string message);      // false → McpException(message)
  }
  public sealed partial class ExodusApiClient { public Task<MeDto> GetMeAsync(CancellationToken ct); } // GET api/auth/me
  public static class ServiceCollectionExtensions
  {
      // AddExodusApi + AddMcpServer(ServerInfo {Name="exodus-mcp", Version="0.1.0"}, ServerInstructions = profile metni)
      // + CommonTools + profile'a göre tool/prompt/resource sınıfları (sonraki task'lar buraya ekler)
      public static IMcpServerBuilder AddExodusMcp(this IServiceCollection services, ExodusOptions options);
  }
  public sealed class CommonTools(ExodusApiClient api, ExodusOptions options)  // whoami
  ```
- `Translate` eşlemesi. `McpException` olduğu gibi geçiyor; iptal edilmiş token'ın `OperationCanceledException`'ı yeniden fırlatılıyor.

  | Hata | Mesaj |
  |---|---|
  | `HttpRequestException` | `Exodus'a ulaşılamıyor (EXODUS_BASE_URL=<url>). API çalışıyor mu?` |
  | timeout (`TaskCanceledException`, token iptal edilmemiş) | `Exodus 30 saniye içinde yanıt vermedi (EXODUS_BASE_URL=<url>).` |
  | `ExodusAuthException` | kendi mesajı |
  | 400 | `Exodus isteği reddetti: <detail>` |
  | 401 | `Exodus oturumu doğrulanamadı. MCP sunucusunu yeniden başlatmayı deneyin.` |
  | 403 | `forbidden ?? "Bu işlem için yetkiniz yok: <detail>"` |
  | 404 | `notFound ?? "Kayıt bulunamadı: <detail>"` |
  | 409 | `İşlem çakıştı: <detail>` |
  | 429 | `Exodus rate limit'e takıldı, biraz bekleyip tekrar deneyin.` |
  | ≥500 | `Exodus beklenmeyen bir hata döndürdü (HTTP <status>).` |
  | diğer | `Beklenmeyen hata: <ExceptionType>`; mesaj sızdırılmıyor |

  `<detail>` null ise `(ayrıntı yok)` yazılıyor.
- Annotation tablosu (tüm task'lar için bağlayıcı; `OpenWorld=false` hepsinde):

  | Tür | ReadOnly | Destructive | Idempotent | Araçlar |
  |---|---|---|---|---|
  | Okuma | true | false | true | whoami, search_listings, get_listing, list_categories, get_cart, list_my_addresses, list_my_orders, get_order, list_my_listings, list_seller_orders, get_seller_order, sales_summary |
  | Ayarlayan yazma | false | false | true | update_cart_item, remove_from_cart, update_listing, update_stock, confirm_order |
  | Ekleyen yazma | false | false | false | add_to_cart, place_order, create_listing, ship_order, mark_delivered |
  | Yıkıcı | false | true | false | cancel_order |

- `whoami` çıktısı: `{ userId, username, email, role, profile, exodusBaseUrl }`.
- ServerInstructions (Türkçe, kısa):
  - **customer:** sipariş vermeden önce sepet, adres ve tutar özetini gösterip açık onay al; kart bilgisi isteme; ödeme kapıda ödeme olarak kaydedilir.
  - **seller:** listing ve sipariş yönetimi; kargo için taşıyıcı ve takip no zorunlu.
- McpTestHost:
  ```csharp
  public sealed class McpTestHost : IAsyncDisposable
  {
      // ServiceCollection + AddLogging + (time ?? TimeProvider.System) singleton + AddExodusMcp(options)
      // .WithStreamServerTransport(pipe streams); ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => fake))
      // McpClient.CreateAsync(StreamClientTransport, new McpClientOptions { DiscoverProbeTimeout = 30 s })
      public static Task<McpTestHost> StartAsync(ExodusProfile profile, FakeExodusHandler fake, TimeProvider? time = null);
      public McpClient Client { get; }
      public Task<ToolCallOutcome> CallAsync(string tool, Dictionary<string, object?>? args = null); // record ToolCallOutcome(bool IsError, string Text)
  }
  ```
  Kullanılan seçenekler: `BaseUrl` `http://exodus.test/`, `Password` `"pw"`. `Username` profile göre `ahmet@test.com` ya da `zeynep@test.com`. `IsError`, `result.IsError == true` ile hesaplanıyor.

- [ ] **Step 1: Failing testler**
  - **ToolInput:**
    - `ToolInput_OneOf_NormalizesCase`: `"likenew"` → `"LikeNew"`.
    - `ToolInput_OneOf_InvalidListsAllowedValues`: mesaj `condition` adını ve `New, LikeNew, Used, Refurbished` değerlerini içeriyor.
    - `ToolInput_Date_RejectsOtherFormats`: `18.11.2026` reddediliyor.
  - **ToolErrors:**
    - `ToolErrors_MapsStatusCodes`: `[Theory]`, her satır için beklenen mesaj başlangıcı.
    - `ToolErrors_UnknownException_DoesNotLeakMessage`.
  - **Protokol:**
    - `Whoami_ReturnsProfileAndUser`: fake `GET /api/auth/me` → `{"userId":"7","username":"ahmet","email":"ahmet@test.com","role":"Customer"}`. Metin `"profile":"Customer"` içeriyor ve `"pw"` içermiyor.
    - `Whoami_IsReadOnlyIdempotentClosedWorld`: annotation'lar tablodaki gibi.
    - `ConnectionRefused_MapsToUnreachableMessage`: `OnThrow` ile login'de `HttpRequestException` → `IsError`, metin `Exodus'a ulaşılamıyor (EXODUS_BASE_URL=http://exodus.test/)` içeriyor.
    - `ServerError_MapsToHttpStatusMessage`: `/api/auth/me` 500 dönüyor.
    - `ToolOutput_DoesNotEscapeTurkish`: username `"Işıl"` metinde aynen görünüyor.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(tools): error translation, JSON output, whoami and in-memory protocol host`.

### Task 7 (MCP): Müşteri okuma araçları

**Files:**
- Create: `src/Exodus.Mcp.Core/Api/Dtos/CatalogDtos.cs`, `src/Exodus.Mcp.Core/Api/Dtos/OrderDtos.cs`, `src/Exodus.Mcp.Core/Api/ExodusApiClient.Catalog.cs`, `src/Exodus.Mcp.Core/Api/ExodusApiClient.Orders.cs`, `src/Exodus.Mcp.Core/Tools/CustomerTools.cs`.
- Modify: `ServiceCollectionExtensions.cs`; customer profilinde `WithTools<CustomerTools>()` çağrısı ekleniyor.
- Test: `tests/Exodus.Mcp.Tests/Protocol/CustomerReadToolsTests.cs`.

**Interfaces:**
- Consumes: `ToolErrors`, `ToolInput`, `ToolJson`, `McpTestHost`.
- Produces: `ExodusApiClient` metotları.
  - `ListingSearchQuery` (CatalogDtos.cs): `record ListingSearchQuery(string? Search, int? CategoryId, decimal? MinPrice, decimal? MaxPrice, string? Condition, string SortBy, string SortOrder, int Page, int PageSize)`. Değerler Exodus parametre değerleri; `newest` → `createdAt` dönüşümü araçta yapılıyor.
  - `SearchListingsAsync(ListingSearchQuery q, ct)` → `GET api/listings/search?search=&categoryId=&minPrice=&maxPrice=&condition=&sortBy=&sortOrder=&page=&pageSize=10`.
  - `GetListingAsync(int id, ct)` → `GET api/listings/{id}`.
  - `GetProductAsync(int id, ct)` → `GET api/product/{id}`.
  - `GetCategoryTreeAsync(ct)` → `GET api/category/tree`; yanıt çıplak dizi.
  - `GetAddressesAsync(ct)` → `GET api/address`.
  - `GetOrdersAsync(string? status, int page, int pageSize, ct)` → `GET api/order?status=&page=&pageSize=10`.
  - `GetOrderAsync(int id, ct)` → `GET api/order/{id}`.
  - `GetOrderByNumberAsync(string orderNumber, ct)` → `GET api/order/number/{escaped}`.
- Exodus alan adları (bkz. Kaynak notları):
  - Arama item'ı `stockQuantity` kullanıyor, `GET api/listings/{id}` ise `stock`.
  - Sipariş listesinde `totalPages` yok.
- Araçlar ve çıktıları:

  | Araç (parametreler) | Kurallar | Çıktı |
  |---|---|---|
  | `search_listings(query?, categoryId?, minPrice?, maxPrice?, condition?, sortBy="price", sortOrder="asc", page=1)` | `sortBy` ∈ `price,newest,name` (Exodus karşılıkları `price,createdAt,name`). `condition` ∈ `New,LikeNew,Used,Refurbished`. `minPrice > maxPrice` ise hata. `page >= 1` | `{page,totalPages,totalCount,items:[{listingId,productName,price,stock,stockStatus,condition,seller,category}]}` |
  | `get_listing(listingId)` | Listing, ardından product çekiliyor. 404 → `Listing bulunamadı (id=<id>)` | `{listingId,productId,productName,description,price,stock,condition,isActive,sellerId}` |
  | `list_categories()` | — | `[{id,name,productCount,children:[…]}]` |
  | `list_my_addresses()` | — | `[{addressId,title,fullName,fullAddress,isDefault,type}]` |
  | `list_my_orders(status?, page=1)` | `status` ∈ Exodus `OrderStatus` değerleri | `{page,totalCount,items:[{orderId,orderNumber,status,totalAmount,currency,itemCount,createdAt}]}` |
  | `get_order(orderId?, orderNumber?)` | İkisinden tam olarak biri verilmeli. 404 → `Sipariş bulunamadı (<id veya numara>)` | `{orderId,orderNumber,status,totalAmount,currency,createdAt,paidAt,shippedAt,deliveredAt,shippingAddress,sellerOrders:[{sellerOrderId,sellerName,status,total,items:[{productName,quantity,unitPrice}],shipment:{carrier,trackingNumber,shippedAt,deliveredAt}}]}` |

  Her araç ve her parametre Türkçe `[Description]` taşıyor. Açıklamada geçerli değerler ve varsayılanlar yazılıyor.

- [ ] **Step 1: Failing testler** (`McpTestHost`, customer profili)
  - `SearchListings_BuildsQueryAndMapsItems`: `query="kulaklık", maxPrice=1500, sortBy="price"` → istek `/api/listings/search?search=kulakl%C4%B1k&maxPrice=1500&sortBy=price&sortOrder=asc&page=1&pageSize=10`. `items[0].seller` alanı `sellerName`'den geliyor.
  - `SearchListings_FormatsDecimalsInvariant_UnderTurkishCulture`: `minPrice=1500.5`, tr-TR altında → `minPrice=1500.5`.
  - `SearchListings_NormalizesCondition`: `"likenew"` → `condition=LikeNew`.
  - `SearchListings_NewestMapsToCreatedAtDesc`: `sortBy="newest", sortOrder="desc"` → `sortBy=createdAt&sortOrder=desc`.
  - `SearchListings_MinGreaterThanMax_IsErrorWithoutRequest`.
  - `GetListing_CombinesListingAndProduct`.
  - `GetListing_NotFound_TurkishMessage`.
  - `GetOrder_RequiresExactlyOneIdentifier`: ikisi birden ya da hiçbiri → `IsError`, istek atılmıyor.
  - `GetOrder_ByNumber_EscapesPath`.
  - `ListMyOrders_PassesStatusFilter`: `"shipped"` → `status=Shipped`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(customer): catalog, address and order read tools`.

### Task 8 (MCP): Müşteri sepet ve sipariş yazma araçları

**Files:**
- Create: `src/Exodus.Mcp.Core/Api/Dtos/CartDtos.cs`, `src/Exodus.Mcp.Core/Api/ExodusApiClient.Cart.cs`. Checkout, payment ve cancel metotları `ExodusApiClient.Orders.cs`'e ekleniyor.
- Modify: `src/Exodus.Mcp.Core/Tools/CustomerTools.cs`.
- Test: `tests/Exodus.Mcp.Tests/Protocol/CustomerWriteToolsTests.cs`.

**Interfaces:**
- Consumes: `TokenStore.GetSessionAsync(ct).UserId`; sepet yolları bunu istiyor. Task 7'deki sipariş DTO'ları da kullanılıyor.
- Produces: `ExodusApiClient` metotları.
  - `GetCartAsync(ct)` → `GET api/cart/my`.
  - `AddToCartAsync(int userId, int listingId, int quantity, ct)` → `POST api/cart/add {userId, listingId, quantity}`.
  - `UpdateCartItemAsync(int userId, int cartItemId, int quantity, ct)` → `PUT api/cart/{userId}/item/{cartItemId} {quantity}`.
  - `RemoveCartItemAsync(int userId, int cartItemId, ct)` → `DELETE api/cart/{userId}/item/{cartItemId}`; 200 dönüyor ve gövdede sepet var.
  - `CheckoutAsync(int shippingAddressId, int? billingAddressId, string? customerNote, ct)` → `POST api/order/checkout`, 201.
  - `CreateCashOnDeliveryIntentAsync(int orderId, ct)` → `POST api/payment/intents {orderId, method:"CashOnDelivery"}`.
  - `CancelOrderAsync(int orderId, string reason, string? note, ct)` → `POST api/order/{id}/cancel {reason, note}`.
- Araçlar:

  | Araç (parametreler) | Kurallar | Çıktı |
  |---|---|---|
  | `get_cart()` | — | `{total,items:[{cartItemId,listingId,productName,unitPrice,quantity,lineTotal}]}` |
  | `add_to_cart(listingId, quantity=1)` | `1 <= quantity <= 99`. `userId` oturumdan alınıyor, parametre değil | sepet (`get_cart` ile aynı şekil) |
  | `update_cart_item(cartItemId, quantity)` | `quantity >= 1`. Değilse: `Miktarı 0 yapmak için remove_from_cart kullanın.` | sepet |
  | `remove_from_cart(cartItemId)` | — | sepet |
  | `place_order(shippingAddressId, billingAddressId?, note?)` | Checkout, ardından COD intent | `{orderId,orderNumber,status,totalAmount,currency,sellerOrders:[{sellerOrderId,sellerName,total}],payment:{method:"CashOnDelivery",status}}` |
  | `cancel_order(orderId, reason="CustomerRequest", note?)` | `reason` ∈ `CustomerRequest,OutOfStock,PaymentFailed,FraudSuspected,SellerCancelled,DeliveryIssue,Other` | `get_order` ile aynı şekil |

  - **Kısmi hata:** checkout başarılı olup intent başarısız olursa `McpException` fırlatılıyor: `Sipariş oluşturuldu (<orderNumber>, id=<orderId>) ancak kapıda ödeme kaydı oluşturulamadı: <çevrilmiş hata>. Siparişi tekrar oluşturmayın; get_order ile durumu kontrol edin.`
  - **Açıklamalar:** `place_order` açıklaması, kart bilgisi istenmediğini ve onay alınmadan çağrılmaması gerektiğini söylüyor.

- [ ] **Step 1: Failing testler**
  - `AddToCart_SendsSessionUserId`: login `userId=7` → gövde `{"userId":7,"listingId":3,"quantity":2}`. Araç şemasında `userId` parametresi yok.
  - `UpdateCartItem_ZeroQuantity_IsErrorWithoutRequest`.
  - `RemoveFromCart_UsesUserIdInPath`: `/api/cart/7/item/11`.
  - `PlaceOrder_ChecksOutThenCreatesCodIntent`: iki istek bu sırayla gidiyor. Intent gövdesi `"method":"CashOnDelivery"`. Çıktıda `orderNumber` ve `payment.status:"Created"` var.
  - `PlaceOrder_PaymentFailure_ReportsCreatedOrder_NoRetry`: intent 500 → `IsError`, metin `ORD-` ile başlayan numarayı ve `tekrar oluşturmayın` ifadesini içeriyor. Checkout istek sayısı 1.
  - `PlaceOrder_SchemaHasNoCardOrCouponFields`: `inputSchema.properties` anahtarları tam olarak `shippingAddressId, billingAddressId, note`.
  - `CancelOrder_DefaultReason_AndIsDestructive`: gövde `"reason":"CustomerRequest"`, `DestructiveHint == true`.
  - `CancelOrder_ExodusRejects_ShowsDetail`: 400 ve `{"detail":"Kargoya verilmiş siparişler iptal edilemez."}` → metin bu ayrıntıyı içeriyor.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(customer): cart, place_order (cash on delivery) and cancel_order`.

### Task 9 (MCP): Satıcı listing araçları

**Files:**
- Create: `src/Exodus.Mcp.Core/Api/Dtos/SellerDtos.cs`, `src/Exodus.Mcp.Core/Api/ExodusApiClient.Seller.cs`, `src/Exodus.Mcp.Core/Tools/SellerTools.cs`.
- Modify: `ServiceCollectionExtensions.cs`; seller profilinde `WithTools<SellerTools>()` çağrısı ekleniyor.
- Test: `tests/Exodus.Mcp.Tests/Protocol/SellerListingToolsTests.cs`.

**Interfaces:**
- Consumes: Task 2'nin `POST api/seller/listings` 201 gövdesi (`SellerListingItemDto` şekli).
- Produces: `ExodusApiClient` metotları.
  - `GetMyListingsAsync(bool? isActive, int page, int pageSize, ct)` → `GET api/seller/listings?isActive=&page=&pageSize=20`; yanıt sayfalı.
  - `GetLowStockAsync(ct)` → `GET api/seller/listings/low-stock`; çıplak dizi.
  - `GetOutOfStockAsync(ct)` → `GET api/seller/listings/out-of-stock`.
  - `CreateProductAsync(string name, string description, string barcode, ct)` → `POST api/product {productName, productDescription, barcodes:[barcode]}`, 201.
  - `CreateSellerListingAsync(int productId, decimal price, int stockQuantity, string condition, ct)` → `POST api/seller/listings`, 201 + gövde.
  - `UpdateSellerListingAsync(int id, decimal? price, bool? isActive, int? lowStockThreshold, ct)` → `PUT api/seller/listings/{id}`; null alanlar gönderilmiyor.
  - `UpdateStockAsync(int id, int stockQuantity, ct)` → `PATCH api/seller/listings/{id}/stock`, yanıt `{stockQuantity, stockStatus}`.
- Araçlar:

  | Araç (parametreler) | Kurallar | Çıktı |
  |---|---|---|
  | `list_my_listings(stockFilter="all", isActive?, page=1)` | `stockFilter` ∈ `all,low,out`. `low` ve `out` için `isActive` ve `page` kullanılmıyor | `{filter,page?,totalPages?,totalCount,items:[…]}`. `all` item'ı: `{listingId,productName,price,stock,stockStatus,lowStockThreshold,condition,isActive}`. `low`: `{listingId,productName,stock,lowStockThreshold}`. `out`: `{listingId,productName,isActive}` |
  | `create_listing(price, stockQuantity, productId?, newProductName?, newProductDescription?, newProductBarcode?, condition="New")` | `productId` ile `newProductName`'den tam olarak biri verilmeli. `newProductName` verildiyse `newProductBarcode` zorunlu. `price > 0`, `stockQuantity >= 0` | `{listingId,productId,productName,price,stock,stockStatus,condition,isActive,createdProductId?}` |
  | `update_listing(listingId, price?, isActive?, lowStockThreshold?)` | En az bir alan verilmeli. `price > 0`, `lowStockThreshold >= 0`. 404 → `Listing bulunamadı ya da size ait değil (id=<id>)` | `{listingId,updated:{price?,isActive?,lowStockThreshold?}}` |
  | `update_stock(listingId, stockQuantity)` | `stockQuantity >= 0`. Mutlak değer, delta değil (açıklamada yazıyor) | `{listingId,stock,stockStatus}` |

  - **Kısmi hata:** ürün oluşturulup listing oluşturulamazsa mesaj şu: `Ürün oluşturuldu (productId=<id>) ancak listing oluşturulamadı: <çevrilmiş hata>. Tekrar denerken productId=<id> kullanın.`

- [ ] **Step 1: Failing testler** (seller profili; login rolü `Seller`)
  - `ListMyListings_Low_UsesLowStockRoute`.
  - `ListMyListings_All_PassesIsActiveAndPage`.
  - `CreateListing_WithExistingProduct_SingleRequest`.
  - `CreateListing_WithNewProduct_CreatesProductThenListing`: iki istek gidiyor. Ürün gövdesinde `barcodes:["8690000000001"]` var. Çıktıda `createdProductId` dolu.
  - `CreateListing_NewProductWithoutBarcode_IsErrorWithoutRequest`.
  - `CreateListing_BothProductIdAndName_IsError`.
  - `CreateListing_ListingFails_ReportsCreatedProductId`.
  - `UpdateListing_SendsOnlyProvidedFields`: gövde yalnızca `{"price":249.9}`.
  - `UpdateListing_NothingToUpdate_IsError`.
  - `UpdateStock_Negative_IsErrorWithoutRequest`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(seller): listing tools`.

### Task 10 (MCP): Satıcı sipariş araçları ve `sales_summary`

**Files:**
- Create: `src/Exodus.Mcp.Core/Tools/SalesSummaryCalculator.cs`.
- Modify: `SellerDtos.cs`, `ExodusApiClient.Seller.cs`, `SellerTools.cs`.
- Test: `tests/Exodus.Mcp.Tests/Tools/SalesSummaryCalculatorTests.cs`, `tests/Exodus.Mcp.Tests/Protocol/SellerOrderToolsTests.cs`.

**Interfaces:**
- Consumes: Task 1'in JSON'u (`SellerOrderListItemDto` şekli).
- Produces: MCP tarafında `SellerOrderDto` (SellerDtos.cs). Bu, Task 1 JSON'unun birebir record karşılığı; `Items` tipi `SellerOrderLineDto`, `Shipment` tipi `SellerShipmentDto?`.
- Produces: `ExodusApiClient` metotları.
  - `GetSellerOrdersAsync(string? status, int page, int pageSize, ct)` → `GET api/seller/orders?status=&page=&pageSize=`; çıplak dizi, dönüş tipi `IReadOnlyList<SellerOrderDto>`.
  - `GetSellerOrderAsync(int id, ct)` → `GET api/seller/orders/{id}`, dönüş tipi `SellerOrderDto`.
  - `GetShipmentTimelineAsync(int sellerOrderId, ct)` → `GET api/shipments/seller-orders/{id}/timeline`.
  - `ConfirmSellerOrderAsync(int id, ct)` → `POST api/seller/orders/{id}/confirm`.
  - `ShipSellerOrderAsync(int id, string carrier, string trackingNumber, ct)` → `POST api/seller/orders/{id}/ship`.
  - `MarkDeliveredAsync(int id, ct)` → `PATCH api/shipments/seller-orders/{id}/deliver`.
- Hesaplayıcı:
  ```csharp
  public sealed record SalesSummary(DateOnly From, DateOnly To, int OrderCount, decimal Revenue, int CancelledCount,
      IReadOnlyDictionary<string, int> StatusBreakdown, IReadOnlyList<TopListing> TopListings, bool Truncated);
  public sealed record TopListing(int ListingId, string ProductName, int Quantity, decimal Revenue);
  public static class SalesSummaryCalculator
  {
      // createdAt UTC kabul edilir (Kind=Unspecified → Utc). Aralık: [from 00:00Z, to+1 00:00Z).
      // Revenue ve TopListings iptal edilenleri hariç tutar. TopListings: quantity desc, sonra revenue desc, ilk 5.
      public static SalesSummary Calculate(IEnumerable<SellerOrderDto> orders, DateOnly from, DateOnly to, bool truncated);
  }
  ```
- Araçlar:

  | Araç (parametreler) | Kurallar | Çıktı |
  |---|---|---|
  | `list_seller_orders(status?, page=1)` | `status` ∈ `Pending,Confirmed,Processing,Shipped,Delivered,Cancelled`; açıklamada `Pending` = onay bekliyor. Sayfa boyutu 20 | `{page,hasMore,items:[{sellerOrderId,orderNumber,status,total,itemCount,createdAt}]}`. `hasMore = items.Count == 20` |
  | `get_seller_order(sellerOrderId)` | `shipment` doluysa timeline da çekiliyor. 404 → `Satıcı siparişi bulunamadı (id=<id>)` | `{sellerOrderId,orderNumber,status,total,createdAt,items:[{listingId,productName,quantity,unitPrice,totalPrice}],shipment?,timeline?:[{status,at}]}` |
  | `confirm_order(sellerOrderId)` | İşlemden sonra `get_seller_order` görünümü yeniden çekiliyor | `get_seller_order` şekli |
  | `ship_order(sellerOrderId, carrier, trackingNumber)` | İkisi de boş olamaz. 403 → `Bu satıcı siparişi size ait değil (id=<id>)` | `get_seller_order` şekli |
  | `mark_delivered(sellerOrderId)` | Exodus'tan gelen `{message}` 400'ü ayrıntıyla gösteriliyor | `get_seller_order` şekli |
  | `sales_summary(fromDate?, toDate?)` | Varsayılan aralık: `to` bugün (UTC), `from` = `to` − 29 gün. `from > to` ise hata | `{from,to,orderCount,revenue,cancelledCount,statusBreakdown,topListings:[{listingId,productName,quantity,revenue}],truncated}` |

  - **`sales_summary` sayfalama:** `pageSize=100` ile sayfa 1'den başlanıyor. Döngü üç durumda duruyor: sayfa 100'den az kayıt dönerse, sayfanın en eski `createdAt`'i `from`'dan önceyse ya da 20 sayfaya ulaşılırsa. Üçüncü durumda `truncated=true`.

- [ ] **Step 1: Failing testler**
  - **Hesaplayıcı:** Aralık `2026-11-10..2026-11-15`. Fikstür:

    | Sipariş | createdAt | Durum | Toplam | Kalemler |
    |---|---|---|---|---|
    | A | `2026-11-10T00:00:00` | Delivered | 100 | listing 1 × 2 @ 50 |
    | B | `2026-11-12T08:00:00` | Cancelled | 40 | listing 2 × 1 |
    | C | `2026-11-15T23:59:59`, Kind=Unspecified | Pending | 60 | listing 1 × 1 @ 60 |
    | D | `2026-11-16T00:00:00` | Shipped | 30 | listing 3 × 3 @ 10 |

    Beklenen: `OrderCount 3`, `Revenue 160`, `CancelledCount 1`, `StatusBreakdown {Delivered:1, Cancelled:1, Pending:1}`, `TopListings[0] = (1, …, 3, 160)`, `TopListings.Count == 1`.
    - `Calculate_FixtureTotals`: yukarıdaki değerleri doğruluyor.
    - `Calculate_EmptyInput_ZeroesNotNulls`.
  - **Protokol:**
    - `ListSellerOrders_MapsOrderNumberAndHasMore`.
    - `GetSellerOrder_FetchesTimelineOnlyWhenShipped`.
    - `ConfirmOrder_RefetchesOrder`: istekler `POST confirm`, ardından `GET /api/seller/orders/{id}`.
    - `ShipOrder_RequiresCarrierAndTracking`.
    - `ShipOrder_Forbidden_TurkishOwnershipMessage`.
    - `MarkDelivered_BeforeShip_ShowsExodusMessage`: 400 ve `{"message":"Shipment yok. Önce ship yap."}` → metin bu mesajı içeriyor.
    - `SalesSummary_StopsPagingAtFromDate`: sayfa 1'de 100 kayıt var ve sonuncusu `from`'dan eski → tek istek.
    - `SalesSummary_RejectsFromAfterTo`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat(seller): order tools and sales_summary`.

### Task 11 (MCP): Prompt'lar, resource ve profil bazlı kayıt sözleşmesi

**Files:**
- Create: `src/Exodus.Mcp.Core/Prompts/ExodusPrompts.cs` (`CustomerPrompts` + `SellerPrompts`), `src/Exodus.Mcp.Core/Resources/CatalogResources.cs`.
- Modify: `ServiceCollectionExtensions.cs`.
- Test: `tests/Exodus.Mcp.Tests/Protocol/RegistrationContractTests.cs`, `tests/Exodus.Mcp.Tests/Protocol/PromptsAndResourcesTests.cs`.

**Interfaces:**
- **Prompt'lar:**
  - `shopping_assistant(goal?)`, yalnızca customer profilinde. Tek bir user mesajı dönüyor.
    - Akış: ara → karşılaştır → sepete ekle → `list_my_addresses` ile adresi teyit et → sepet, adres ve toplam özetini göster → **açık onay almadan `place_order` çağırma** → kart bilgisi isteme.
    - `goal` verildiyse metne ekleniyor.
  - `seller_daily_report()`, yalnızca seller profilinde. Modele üç çağrı yapmasını söylüyor, ardından Türkçe kısa bir rapor yazmasını istiyor:
    - `list_my_listings(stockFilter="low")` ve `list_my_listings(stockFilter="out")`
    - `list_seller_orders(status="Pending")`
    - `sales_summary(fromDate=<bugün−6>, toDate=<bugün>)`; tarihler prompt üretilirken UTC'ye göre `yyyy-MM-dd` olarak yazılıyor.
- **Resource:** `exodus://categories`, `MimeType = "application/json"`, her iki profilde kayıtlı. `list_categories` ile aynı JSON'u `string` olarak dönüyor; SDK başka dönüş tiplerini reddediyor. Hata durumunda `ToolErrors.Translate` sonucu fırlatılıyor.
- **Kayıt sözleşmesi:**
  - customer araçları (13): `whoami, search_listings, get_listing, list_categories, get_cart, add_to_cart, update_cart_item, remove_from_cart, list_my_addresses, place_order, list_my_orders, get_order, cancel_order`.
  - seller araçları (11): `whoami, list_my_listings, create_listing, update_listing, update_stock, list_seller_orders, get_seller_order, confirm_order, ship_order, mark_delivered, sales_summary`.

- [ ] **Step 1: Failing testler**
  - **Kayıt sözleşmesi:**
    - `CustomerProfile_ExposesExactlyCustomerTools`: küme eşitliği, ve hiçbir seller aracı görünmüyor.
    - `SellerProfile_ExposesExactlySellerTools`.
    - `EveryTool_HasExplicitAnnotationsMatchingTable`: her iki profil için Task 6 tablosu üzerinden `[Theory]`.
    - `EveryToolAndParameter_HasDescription`: `JsonSchema.properties.*.description` alanları boş değil.
    - `OptionalParameters_AreNotRequired`: `search_listings` şemasında `required` dizisi yok ya da boş. `get_listing.required == ["listingId"]`.
    - `ServerInstructions_AreProfileSpecific`.
  - **Prompt ve resource:**
    - `Prompts_ArePerProfile`.
    - `SellerDailyReport_MentionsToolsAndDates`: metin `list_my_listings`, `list_seller_orders`, `sales_summary` ve `ManualTimeProvider` tarihini içeriyor.
    - `ShoppingAssistant_RequiresConfirmationBeforePlaceOrder`: metin `place_order` ve `onay` içeriyor.
    - `CategoriesResource_ReturnsJson_BothProfiles`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Tüm testler geçiyor.
- [ ] **Step 5:** Commit `feat: prompts, categories resource and per-profile registration contract`.

### Task 12 (MCP): Stdio host, gerçek süreç testleri ve opt-in canlı senaryo

**Files:**
- Create: `src/Exodus.Mcp.Stdio/Exodus.Mcp.Stdio.csproj` (`OutputType=Exe`, `AssemblyName=Exodus.Mcp.Stdio`), `src/Exodus.Mcp.Stdio/Program.cs`, `tests/Exodus.Mcp.Tests/Stdio/StdioHostTests.cs`, `tests/Exodus.Mcp.Tests/Live/LiveFactAttribute.cs`, `tests/Exodus.Mcp.Tests/Live/LiveScenarioTests.cs`.
- Modify: `Exodus.Mcp.sln` (Stdio projesi ekleniyor). Test projesi Stdio projesine `ProjectReference` veriyor; böylece dll test çıktısına kopyalanıyor.

**Interfaces:**
- `Program.cs`:
  - `Host.CreateApplicationBuilder(args)`.
  - Loglar stderr'e: `AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)`.
  - `ExodusOptions.FromConfiguration(builder.Configuration)`. `ExodusConfigurationException` gelirse mesaj stderr'e yazılıyor ve exit code 2 dönüyor.
  - `builder.Services.AddExodusMcp(options).WithStdioServerTransport()`, ardından `RunAsync`.
  - Başlangıçta ağa çıkılmıyor.
- Süreç testleri `ModelContextProtocol.Client.StdioClientTransport` kullanıyor:
  - Seçenekler: `Command = "dotnet"`, `Arguments = [<AppContext.BaseDirectory>/Exodus.Mcp.Stdio.dll]`, `EnvironmentVariables`.
  - Bu tip adlarını SDK 2.4.0'da doğrula; farklıysa aynı işi yapan karşılığını kullan ve report'ta belirt.
- `LiveFactAttribute : FactAttribute`: `EXODUS_LIVE` ortam değişkeni `1` değilse `Skip = "Set EXODUS_LIVE=1 with Exodus running on EXODUS_BASE_URL"`.

- [ ] **Step 1: Failing testler**
  - `StdioHost_StartsWithoutExodus_AndReportsUnreachable`:
    - Ortam: `EXODUS_BASE_URL=http://127.0.0.1:9`, profil customer, kullanıcı adı ve şifre dolu.
    - `ListToolsAsync` 13 araç dönüyor.
    - `search_listings` → `IsError`, metin `Exodus'a ulaşılamıyor` içeriyor.
  - `StdioHost_InvalidProfile_ExitsWithCode2AndMessage`: `EXODUS_PROFILE=admin` ile süreci doğrudan `Process` olarak başlat. Exit code 2, stderr `EXODUS_PROFILE` içeriyor, stdout boş.
  - `[LiveFact] CustomerAndSeller_EndToEnd` (spec Doğrulama B.4'ün LLM'siz hali; iki stdio süreci: ahmet@test.com / zeynep@test.com, `Demo123!`):
    1. customer `search_listings(query:"kulaklık", maxPrice:1500, sortBy:"price")` → Zeynep'in bir listing'ini seç.
    2. `add_to_cart`.
    3. `list_my_addresses` → varsayılan adres.
    4. `place_order` → `orderNumber`.
    5. seller `list_seller_orders(status:"Pending")` → aynı `orderNumber` görünüyor.
    6. `confirm_order`.
    7. `ship_order(carrier:"Yurtici", trackingNumber:"TR123")`.
    8. customer `get_order(orderNumber)` → `status == "Shipped"`.
    9. seller `sales_summary()` → `orderCount >= 1`.
- [ ] **Step 2:** Testleri çalıştır, FAIL gör (proje yok). Live test `Skipped` görünmeli.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4: Doğrula.**
  - `dotnet test Exodus.Mcp.sln` → yeşil, live test skipped.
  - Exodus lokalde çalışıyorsa (`dotnet run --launch-profile http`, SQL container açık) şunu da çalıştır: `EXODUS_LIVE=1 EXODUS_BASE_URL=http://localhost:5013 dotnet test Exodus.Mcp.sln --filter FullyQualifiedName~LiveScenarioTests` → PASS. Exodus'u başlatmak ya da DB'yi sıfırlamak gerekirse yalnızca controller yapar, implementer yapmaz.
- [ ] **Step 5:** Commit `feat(stdio): console host with startup validation, process and live tests`.

### Task 13 (MCP): README ve dokümantasyon

**Files:**
- Create: `README.md`.

**İçerik (spec "README" bölümü):** README İngilizce yazılıyor.
- Tek paragraflık tanıtım ve demo GIF yeri: `docs/demo.gif`. GIF'i kullanıcı kaydedecek; dosya yoksa README'de "coming soon" notu kalıyor.
- Mermaid mimari diyagramı: Claude → MCP (stdio) → Exodus API → SQL Server.
- Araç tabloları: profil, araç, Exodus ucu ve annotation. Prompt'lar ve resource.
- Kurulum:
  - Exodus'u çalıştırma; Exodus README'sine link.
  - `dotnet build`.
  - `claude mcp add exodus-customer -e EXODUS_PROFILE=customer -e EXODUS_USERNAME=ahmet@test.com -e EXODUS_PASSWORD=Demo123! -- dotnet run --project <path>/src/Exodus.Mcp.Stdio`. Seller için aynı komut `zeynep@test.com` ile.
  - Claude Desktop `mcpServers` JSON örneği.
  - Ortam değişkenleri tablosu.
- Design decisions:
  - Neden iki profil var.
  - Neden kart bilgisi yok (yalnız kapıda ödeme).
  - Token yönetimi: lazy login, rotating refresh, 401'de tek retry, login hatası kilitleniyor.
  - Hataların Türkçe ve eyleme dönük olması.
  - stdout disiplini.
- "Security issues found and fixed in Exodus while building this" bölümü. Her madde bir cümle ve `https://github.com/oytunemre/Exodus/commit/<sha>` linki:
  - ödeme simülasyon uçları herkese açıktı: `bc6c569`, `332e913`
  - eski seller IDOR ve kargo sahipliği: `fdd9b73`, `0f85116`
  - webhook imzası: `22aaf4d`
  - ödeme sipariş durumunu geri almıyor: `47c5550`
  - kargodan sonra iptal engeli ve ileri-yönlü durumlar: `e2978b5`
  - katalog ürünü yalnız Admin düzenleyip silebiliyor: `7c2cff1`
  - Task 2'nin listing doğrulama commit'i (sha uygulama sırasında yazılıyor)
- Testing: `dotnet test`. Live senaryonun nasıl çalıştırıldığı (`EXODUS_LIVE=1`). MCP Inspector komutu: `npx @modelcontextprotocol/inspector dotnet run --project src/Exodus.Mcp.Stdio`.
- Roadmap: Aşama 2 (HTTP transport, pass-through JWT).

- [ ] **Step 1:** README'yi yaz. Belgelenen her komutun ve ortam değişkeninin adı kodla birebir aynı olmalı: `grep -n "EXODUS_" README.md` çıktısını `ExodusOptions` ile karşılaştır.
- [ ] **Step 2:** Araç tablosundaki adları Task 11 sözleşmesiyle karşılaştır.
- [ ] **Step 3:** `dotnet build Exodus.Mcp.sln -warnaserror:NU1902,NU1903,NU1904 && dotnet test Exodus.Mcp.sln` → yeşil.
- [ ] **Step 4:** Commit `docs: README with architecture, tools, setup and security notes`.

---

## Plan sonrası (controller ve kullanıcı ile; implementer yapmaz)

1. Her iki dal için final review ve finishing-a-development-branch. Exodus ve MCP reposunda merge ve push **kullanıcı onayıyla** yapılıyor.
2. Canlı doğrulama: SQL container ve Exodus açıkken `EXODUS_LIVE=1` testi çalıştırılıyor.
3. Aşağıdaki iki adım kurulum ya da kullanıcının Claude ayarlarını değiştirme anlamına geldiği için **ayrı onay** gerekiyor:
   - MCP Inspector (`npx`).
   - `claude mcp add`.
4. Demo GIF'ini kullanıcı kaydediyor (spec Doğrulama B.4 senaryosu).
5. GitHub'da `exodus-mcp` reposunu açmak ve push etmek **kullanıcı onayıyla** yapılıyor.
