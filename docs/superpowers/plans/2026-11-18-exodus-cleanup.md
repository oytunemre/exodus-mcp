# Exodus Toparlama (Alt Proje A) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exodus API'yi MCP'ye açılmadan önce güvenli ve demo'ya hazır hale getirmek: PR #4'ü merge etmek, ödeme ve kargo yetki açıklarını kapatmak, public listing araması eklemek, seed verisini düzeltmek, repo hijyeni ve CI.

**Architecture:** Mevcut katmanlar korunuyor (Controller → Service → `ApplicationDbContext`). Sahiplik kontrolleri PR #4'teki `ListingService` kalıbıyla yapılıyor: servis metodu `(…, int callerId, bool isAdmin)` alır, ihlalde `ForbiddenException` fırlatır, middleware bunu 403 ProblemDetails'a çevirir. Her davranış değişikliğinin `Exodus.Tests/E2E` altında bir regresyon testi var.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core, EF Core 10 (SQL Server; testlerde InMemory), xUnit 2.9, FluentAssertions 8, `WebApplicationFactory`.

**Spec:** `/Users/oytunemreozmel/Projects/Exodus MCP/docs/superpowers/specs/2026-11-18-exodus-mcp-design.md` ("Alt proje A" bölümü). Uygulandığı repo: `/Users/oytunemreozmel/Projects/Exodus`.

## Global Constraints

- Ortam: `export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_CLI_UI_LANGUAGE=en`. SDK 10.0.404 kullanılıyor.
- Lokal SQL Server: Docker container `exodus-sql`, `127.0.0.1:1433`. SA şifresi `/private/tmp/claude-502/-Users-oytunemreozmel-Projects-Exodus-MCP/47beb425-8768-4856-8b40-d6b56fc7b94f/scratchpad/sql-sa-password` dosyasında (Task 7'de user-secrets'a taşınacak).
- Çalışma dalı: `chore/mcp-readiness`, merge edilmiş `main`'den açılır. **Push, PR açma ve merge için kullanıcı onayı şart.**
- Testler: `CustomWebApplicationFactory` kullanılıyor (ortam `Development`, InMemory DB). Kullanıcılar `TestHelper.RegisterAndLoginAs{Customer,Seller,Admin}Async(client, suffix)` ile açılıyor. Her task sonunda `dotnet test Exodus.sln` tamamen yeşil olmalı (başlangıç: 402 test).
- Çağıran kimliği: `User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value`. Admin kontrolü: `User.IsInRole("Admin")`.
- Sahiplik ihlali → `ForbiddenException` (403). Bulunamayan kayıt → `NotFoundException` (404). Geçersiz girdi → `BadRequestException` (400). Hepsi `Services/Common/ApiException.cs` içinde.
- Mesaj dili: mevcut koddaki gibi; yeni mesajlar İngilizce.
- Demo şifresi `DbSeeder.DemoPassword = "Demo123!"` değişmiyor. Demo hesapları: satıcılar `zeynep@test.com`, `mehmet@test.com`; müşteriler `ahmet@test.com`, `elif@test.com`, `can@test.com`.

## Spec A6 (kapıda ödeme) — kod değişikliği yok

2026-11-18'de canlı API'de doğrulandı:
- `POST /api/payment/intents` `method=CashOnDelivery` kart bilgisi istemeden oluşuyor (`provider: MANUAL`, `status: Created`).
- Satıcı `Pending` durumdaki alt siparişi `confirm` ve `ship` edebiliyor.

MCP'deki `place_order` bu akışı kullanacak. Task 8'in 6. ve 8. adımları bunu regresyon olarak yeniden doğruluyor.

## Review Focus

1. **Admin bypass:** Sahiplik kontrolü eklenen her uçta admin başkasının kaydına erişebilmeye devam etmeli. Test: Task 3 `GetIntent_AsAdmin_ForeignOrder_ReturnsOk`, Task 4 `GetShipment_AsAdmin_ReturnsOk`.
2. **Türkçe büyük/küçük harf:** `q=kulaklık` araması, "Kulaklık" geçen ürünü bulmalı. Test: Task 5 `Search_IsCaseInsensitive_ForTurkishText`.
3. **Silinmiş ya da pasif kayıtlar:** Soft-delete edilmiş ve `IsActive=false` listing'ler public aramada hiç görünmemeli. Test: Task 5 `Search_ExcludesInactiveAndDeletedListings`.
4. **Tekrarlanan kargo işlemleri:** Teslim edilmiş bir siparişi yeniden kargolamak 500 değil 400 dönmeli. Test: Task 4 `ShipOrder_AfterDelivered_ReturnsBadRequest`.
5. **Aşırı sayfa boyutu:** `pageSize=1000` istenirse 100'e kırpılmalı, `page=0` 1'e çekilmeli. Test: Task 5 `Search_ClampsPaging`.

---

### Task 1: PR #4'ü merge et ve çalışma dalını aç

Doğrulandı: PR #4 dalında build temiz, 402/402 test geçiyor, `Baseline` migration'ı boş SQL Server'a uygulanıyor ve seed kullanıcılar login olabiliyor.

- [ ] **Step 1:** Diff'i oku: `git diff main...origin/devin/1789482413-bug-fixes -- Services/Orders/OrderService.cs Services/Campaigns/CampaignService.cs Services/Common/ExceptionHandlingMiddleware.cs`. Mantık hatası varsa kullanıcıya raporla ve dur.
- [ ] **Step 2:** Kullanıcıdan onay al, ardından `gh pr merge 4 -R oytunemre/Exodus --merge`.
- [ ] **Step 3:** `git switch main && git pull && git switch -c chore/mcp-readiness`.
- [ ] **Step 4:** Test: `dotnet test Exodus.sln`. Beklenen: `Passed: 402, Failed: 0`.

### Task 2: Repo hijyeni + bağımlılık güvenliği + CI

**Files:**
- Modify: `Exodus.csproj`, `Exodus.Tests/Exodus.Tests.csproj` (EF Core paket sürümleri)
- Create: `.github/workflows/ci.yml`
- Untrack: `bin/`, `obj/`, `Exodus.Tests/bin/`, `Exodus.Tests/obj/`, `run_log.txt`, `FarmazonDemo.csproj.user`

- [ ] **Step 1:** Takip edilen build çıktılarını repodan çıkar: `git rm -r --cached -q bin obj Exodus.Tests/bin Exodus.Tests/obj run_log.txt FarmazonDemo.csproj.user`. `.gitignore` bunları zaten kapsıyor; `run_log.txt` satırını ekle.
- [ ] **Step 2:** `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*` ve `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` paketlerini NuGet'teki en son `10.0.x` patch'ine yükselt (ikisi de aynı sürümde olmalı). Hedef: `System.Security.Cryptography.Pkcs 9.0.4` ile gelen NU1902 ve NU1903 uyarılarının kaybolması. Kaybolmazsa `Exodus.csproj`'a doğrudan `System.Security.Cryptography.Pkcs` PackageReference ekle (açığı kapatan en düşük sürüm).
- [ ] **Step 3:** Doğrula: `dotnet build Exodus.sln 2>&1 | grep -c NU190` → `0`. Ardından `dotnet test Exodus.sln` → hepsi geçmeli.
- [ ] **Step 4:** `.github/workflows/ci.yml`: `push` ve `pull_request` tetikleyicileri; `ubuntu-latest`; `actions/setup-dotnet` ile `dotnet-version: 10.0.x`; `dotnet restore`, `dotnet build --no-restore -warnaserror:NU1902,NU1903` ve `dotnet test --no-build`.
- [ ] **Step 5:** Commit: `chore: untrack build output, patch vulnerable transitive dependency, add CI`.

### Task 3: Ödeme uçlarında yetki ve sahiplik

**Files:**
- Modify: `Controllers/PaymentController.cs`, `Services/Payments/IPaymentService.cs`, `Services/Payments/PaymentService.cs`
- Create: `Services/Common/DevelopmentOnlyAttribute.cs`
- Modify (tests): `Exodus.Tests/E2E/TestHelper.cs`, `Exodus.Tests/E2E/PaymentEndpointTests.cs`, `Exodus.Tests/E2E/ShipmentEndpointTests.cs` ve customer token'ıyla `simulate-success`, `authorize`, `capture` veya `intents/{id}/refund` çağıran diğer testler
- Create (tests): `Exodus.Tests/E2E/PaymentAuthorizationTests.cs`

**Interfaces:**
- Produces: `TestHelper.SimulatePaymentSuccessAsAdminAsync(CustomWebApplicationFactory factory, int paymentIntentId) : Task`. Kendi admin client'ını açar; Task 4 testleri bunu kullanır.
- Produces: `IPaymentService` içinde şu metotlar sona `int callerId, bool isAdmin` (CancellationToken'dan önce) parametresi alır: `CreateIntentAsync`, `GetByIdAsync`, `GetByOrderIdAsync`, `CancelAsync`, `GetPaymentEventsAsync`, `Confirm3DSecureAsync`, `ProcessWithGatewayAsync`, `Initialize3DSPaymentAsync`. Kural: `order.BuyerId == callerId || isAdmin`, değilse `ForbiddenException`.
- Yetki tablosu:
  - Customer sahipliğiyle erişebileceği uçlar: `intents` POST, `intents/{id}` GET, `order/{orderId}` GET, `intents/{id}/events`, `intents/{id}/cancel`, `intents/{id}/3ds/confirm`, `gateway/process`, `gateway/3ds/initialize`.
  - Yalnızca Admin (`[Authorize(Policy = "AdminOnly")]`): `authorize`, `capture`, `intents/{id}/refund`, `mark-received` (zaten öyle), `simulate-success`, `simulate-fail`.
  - `simulate-*` ayrıca `[DevelopmentOnly]` taşır.
  - Değişmeyenler: `webhook/{provider}`, `gateway/3ds/complete`, `3ds-callback` (anonim, imza ve iyzico akışı), `gateway/bin`, `gateway/installments`.

- [ ] **Step 1: Failing testleri yaz.** `PaymentAuthorizationTests`'te her test kendi müşteri, satıcı ve siparişini kurar (`ShipmentEndpointTests`'teki setup kalıbı):
  - `CreateIntent_ForForeignOrder_ReturnsForbidden`: müşteri B, A'nın `orderId`'si için intent açmaya çalışır → `403`.
  - `GetIntent_ForForeignOrder_ReturnsForbidden`: B, A'nın intent'ini okumaya çalışır → `403`. Aynı kural `order/{orderId}` ve `events` için de geçerli; tek bir `[Theory]` ile 3 route.
  - `GetIntent_AsAdmin_ForeignOrder_ReturnsOk`: admin → `200`.
  - `Capture_AsCustomer_ReturnsForbidden`: `authorize`, `capture` ve `refund` için `[Theory]` → `403`.
  - `SimulateSuccess_AsCustomer_ReturnsForbidden` → `403`.
  - `SimulateSuccess_AsAdmin_InDevelopment_CapturesPayment`: `200` ve `status == PaymentStatus.Captured`.
  - `DevelopmentOnlyAttribute_InProduction_ReturnsNotFound`: filtreyi doğrudan test et. `ActionExecutingContext` içinde `HttpContext.RequestServices`, `EnvironmentName = "Production"` olan bir `IWebHostEnvironment` stub'ı döner. Sonuç `NotFoundResult` olmalı.
- [ ] **Step 2:** `dotnet test --filter PaymentAuthorizationTests` → beklenen sonuç: Forbidden/NotFound testleri FAIL (bugün 200 dönüyorlar).
- [ ] **Step 3:** `DevelopmentOnlyAttribute : ActionFilterAttribute` yaz. `OnActionExecuting` içinde `IWebHostEnvironment.IsDevelopment()` false ise `context.Result = new NotFoundResult()`.
- [ ] **Step 4:** Servis imzalarını ve sahiplik kontrollerini ekle. Controller'a `GetCurrentUserId()` ve `IsAdmin()` yardımcılarını ekle (`ListingController` ile aynı). Yetki tablosundaki attribute'ları uygula.
- [ ] **Step 5:** `TestHelper.SimulatePaymentSuccessAsAdminAsync` yardımcısını ekle. Mevcut testlerde customer ile yapılan `simulate`, `authorize`, `capture` ve `refund` çağrılarını admin client'a taşı. Çağrı yerlerini `grep -rn -E "simulate-|/authorize|/capture|intents/.*/refund" Exodus.Tests/E2E` ile bul.
- [ ] **Step 6:** `dotnet test Exodus.sln` → hepsi geçmeli.
- [ ] **Step 7:** Commit: `fix(payment): enforce order ownership and admin-only payment state transitions`.

### Task 4: Kargo sahipliği, eski SellerController ve tek bir ship yolu

**Files:**
- Delete: `Controllers/SellerController.cs`
- Modify: `Exodus.Tests/Architecture/ArchitectureTests.cs:79` (`"SellerController"`'ı izin listesinden çıkar)
- Modify: `Services/Shipment/IShipmentService.cs`, `Services/Shipment/ShipmentService.cs`, `Controllers/ShipmentController.cs`, `Controllers/Seller/SellerOrderController.cs`, `Services/Orders/IOrderService.cs`, `Services/Orders/OrderService.cs`
- Create (tests): `Exodus.Tests/E2E/ShipmentOwnershipTests.cs`

**Interfaces:**
- Consumes: `TestHelper.SimulatePaymentSuccessAsAdminAsync` (Task 3).
- Produces: `IShipmentService` metotları `(int sellerOrderId, [ShipSellerOrderDto dto,] int callerId, bool isAdmin, CancellationToken ct = default)` biçimini alıyor. Kural: `sellerOrder.SellerId == callerId || isAdmin`, değilse `ForbiddenException`. `GetBySellerOrderIdAsync` ve `GetTimelineBySellerOrderIdAsync` sahipliği `SellerOrders` üzerinden kontrol ediyor.
- Produces: `IOrderService.RecalculateOrderStatusAsync(int orderId) : Task`. Bugünkü private `SyncOrderStatusWithSellerOrdersAsync` metodunun public hali; `SaveChangesAsync` de çağırıyor. `ShipmentService` artık `IOrderService`'e bağımlı ve `ShipAsync` ile `DeliverAsync` sonunda bu metodu çağırıyor. Döngü yok: `OrderService` yalnızca `INotificationService`'e bağımlı.
- `SellerOrderController.ShipOrder`: `Carrier` ya da `TrackingNumber` boşsa `BadRequestException`. Doluysa `_shipmentService.ShipAsync(id, new ShipSellerOrderDto { … }, sellerId, isAdmin: false)` çağrılıyor; `UpdateSellerOrderStatusAsync` artık çağrılmıyor.
- **Senkronizasyon düzeltmesi** (PR #4 incelemesinde bulunan regresyon). `RecalculateOrderStatusAsync` kuralları:
  - (a) Ana sipariş `Cancelled`, `Failed`, `Refunded`, `PartialRefund` ya da `Completed` ise dokunulmaz.
  - (b) Tüm aktif alt siparişler iptalse → `Cancelled` (yalnızca ana sipariş henüz `Shipped` ya da `Delivered` değilse).
  - (c) Aksi halde türetilen durum, yalnızca mevcut durumdan **ileride** ise uygulanır. Sıra: `Pending(0) < Processing(1) < Confirmed(2) < Shipped(3) < Delivered(4)`. Ödenmiş (`Processing`) bir sipariş asla `Pending`'e düşmez.
- `UpdateSellerOrderStatusAsync`: alt sipariş `Cancelled` ya da `Delivered` ise başka bir duruma geçiş isteği `BadRequestException` ile reddediliyor.

- [ ] **Step 1: Failing testleri yaz** (`ShipmentOwnershipTests`; iki ayrı satıcı, bir müşteri ve her satıcıdan bir ürün içeren tek sipariş):
  - `GetShipment_AsOtherSeller_ReturnsForbidden`: `GET /api/shipments/seller-orders/{id}` ve `/timeline` için `[Theory]` → `403`.
  - `ShipViaShipments_AsOtherSeller_ReturnsForbidden` → `403`.
  - `GetShipment_AsAdmin_ReturnsOk` → `200`.
  - `LegacySellerOrdersRoute_IsGone`: `GET /api/seller/{sellerId}/orders` → `404`.
  - `ShipOrder_WithTracking_CreatesShipment`: `POST /api/seller/orders/{id}/ship` `{carrier:"Yurtici", trackingNumber:"TR123"}` → `200`. Ardından `GET /api/shipments/seller-orders/{id}` → `trackingNumber == "TR123"`, `status == Shipped`. Ardından `PATCH .../deliver` → `200`.
  - `ShipOrder_WithoutTracking_ReturnsBadRequest` → `400`.
  - `ShipOrder_AfterDelivered_ReturnsBadRequest` → `400`.
  - `ParentOrderStatus_FollowsShipmentsPath`: iki seller order'ın ikisi de `/api/shipments/.../ship` ile kargolanınca müşterinin `GET /api/order/{id}` çağrısında `status == "Shipped"` görünmeli; yalnızca biri kargolanınca hâlâ `"Shipped"` değil.
  - `PaidOrder_OneSellerConfirms_DoesNotRegressToPending`: iki satıcılı sipariş admin ile `simulate-success` edilir (`Processing`). Satıcı A `/confirm` eder → ana sipariş `Processing` kalmalı (Pending değil). Satıcı B de `/confirm` edince → `Confirmed`.
  - `CancelledOrder_SellerConfirm_ReturnsBadRequest_AndStaysCancelled`: müşteri `POST /api/order/{id}/cancel` yapar; satıcı `/confirm` → `400`; ana sipariş `Cancelled` kalmalı.
- [ ] **Step 2:** `dotnet test --filter ShipmentOwnershipTests` → FAIL.
- [ ] **Step 3:** Eski controller'ı sil; `IOrderService.RecalculateOrderStatusAsync` metodunu, servis imzalarını ve sahiplik kontrollerini ekle. `ShipmentController`'a `GetCurrentUserId()` ve `IsAdmin()` ekle. `SellerOrderController.ShipOrder`'ı güncelle.
- [ ] **Step 4:** `dotnet test Exodus.sln` → hepsi geçmeli. Mevcut `ShipmentEndpointTests` de sahiplik kuralına uymalı; satıcı kendi siparişini kullanıyor, bozulan olursa düzelt.
- [ ] **Step 5:** Commit: `fix(shipping): enforce seller ownership, remove legacy SellerController, unify ship path`.

### Task 5: Public listing araması

**Files:**
- Create: `Services/Listings/ListingFilters.cs`
- Modify: `Services/Listings/IListingService.cs`, `Services/Listings/ListingService.cs`, `Controllers/ListingController.cs`, `Controllers/Admin/AdminListingController.cs:40-95`, `Models/Dto/ProductDto/ProductSearchDto.cs` (`ListingListItemDto`)
- Create (tests): `Exodus.Tests/E2E/ListingSearchTests.cs`

**Interfaces:**
- Produces: `GET /api/listings/search` (`[AllowAnonymous]`, `[FromQuery] ListingSearchDto`) → `ListingSearchResultDto` (`items`, `totalCount`, `page`, `pageSize`, `totalPages`). Task B'deki MCP aracı `search_listings` bu sözleşmeye bağlı.
- Produces: `ListingListItemDto`'ya yeni alanlar: `string? ProductDescription` (en fazla 200 karakter; uzunsa `…` ile kesilir), `int? CategoryId`, `string? CategoryName`.
- Produces: `static class ListingFilters`:
  - `IQueryable<Listing> Apply(this IQueryable<Listing> q, ListingSearchDto f)` — `Search` (ProductName, ProductDescription, Brand ve Category.Name üzerinde `ToLower().Contains`), `CategoryId`, `ProductId`, `MinPrice`, `MaxPrice`, `Condition`, `SellerId`, `StockStatus`, `InStock`.
  - `IQueryable<Listing> Sort(this IQueryable<Listing> q, string? sortBy, bool desc)` — `price`, `createdAt`, `name`, `stock`, `seller`; varsayılan `createdAt`.
- `AdminListingController.GetListings` aynı filtreleri `ListingFilters` üzerinden kullanacak (davranışı ve imzası değişmiyor; `isActive` filtresi controller'da kalıyor).
- `IListingService.SearchAsync(ListingSearchDto query, CancellationToken ct = default) : Task<ListingSearchResultDto>`:
  - Yalnızca `IsActive` listing'ler; ürünün soft-delete filtresi zaten global.
  - `MinPrice > MaxPrice` ise `BadRequestException`.
  - Sayfalama kırpılıyor: `Page < 1 → 1`; `PageSize < 1 → PaginationNormalizationFilter.DefaultPageSize`; `PageSize > PaginationNormalizationFilter.MaxPageSize → MaxPageSize`.
  - `SortOrder` için `"desc"` → azalan, diğer değerler artan.

- [ ] **Step 1: Failing testleri yaz** (veri doğrudan `_factory.Services` scope'undaki `ApplicationDbContext`'e eklenir; 1 kategori "Elektronik", 3 ürün, 2 satıcı):
  - `Search_Anonymous_ReturnsOk`.
  - `Search_ByQuery_MatchesProductName`.
  - `Search_IsCaseInsensitive_ForTurkishText`: ürün adı `"Bluetooth Kulaklık"`, `q=kulaklık` → 1 sonuç.
  - `Search_ByPriceRange_FiltersInclusive`: fiyatlar 100/500/900, `minPrice=500&maxPrice=900` → 2 sonuç.
  - `Search_ByCategory_ReturnsOnlyCategory`, sonuçta `categoryName == "Elektronik"`.
  - `Search_SortByPriceAsc_IsOrdered`.
  - `Search_ExcludesInactiveAndDeletedListings`.
  - `Search_ClampsPaging`: `pageSize=1000` → `pageSize == 100`; `page=0` → `page == 1`.
  - `Search_MinGreaterThanMax_ReturnsBadRequest` → `400`.
- [ ] **Step 2:** `dotnet test --filter ListingSearchTests` → FAIL (route yok).
- [ ] **Step 3:** `ListingFilters`, `SearchAsync` ve controller action'ını yaz; `AdminListingController`'ı `ListingFilters` kullanacak şekilde düzenle.
- [ ] **Step 4:** `dotnet test Exodus.sln` → hepsi geçmeli; `AdminListingEndpointTests` dahil.
- [ ] **Step 5:** Commit: `feat(listings): public paged listing search with shared filters`.

### Task 6: Demo seed verisi

**Files:**
- Modify: `Data/DbSeeder.cs`
- Create (tests): `Exodus.Tests/E2E/DbSeederTests.cs`. `BugFixRegressionTests.cs:445-459`'daki InMemory `ApplicationDbContext` kurulum kalıbını kullan.

**Kapsam (spec A5):**
- Kullanıcılar aynı kalıyor.
- 5 kategori: Elektronik, Ev & Yaşam, Spor, Kitap, Moda. Slug'lar: `elektronik`, `ev-yasam`, `spor`, `kitap`, `moda`.
- 16 ürün; hepsinde `CategoryId` ve `Brand` dolu, kategori başına en az 3 ürün. Mevcut 5 elektronik ürün bunların içinde.
- Listing'ler **yalnızca** `Role == Seller` olan kullanıcılara ait (zeynep, mehmet). En az 4 ürün iki satıcıda farklı fiyatlarla satılıyor. En az 2 listing `LowStock` (stok ≤ `LowStockThreshold`), 1 listing `OutOfStock` (stok 0). `StockStatus` alanı stokla tutarlı.
- Her müşteri için bir varsayılan adres (`IsDefault = true`, `Type = Both`).
- Demo siparişleri: ahmet ve elif'ten son 10 güne yayılmış 5 sipariş; her biri iki satıcının listing'lerini içeriyor. Durumlar karışık: 2 Delivered, 1 Shipped (Shipment kaydıyla, carrier ve tracking dolu), 1 Confirmed, 1 Pending. Tutarlar `SellerOrderItem` satırlarından hesaplanıyor.
- Sepetler boş başlıyor; mevcut seed'in sepete ürün eklemesi kaldırılıyor.
- Idempotent: anahtarlar e-posta, slug, ürün adı, (seller, product) çifti, (user, address title) çifti ve sipariş `CustomerNote = "SEED-n"`.

- [ ] **Step 1: Failing testleri yaz** (`DbSeederTests`):
  - `Seed_ListingsBelongOnlyToSellers`.
  - `Seed_EveryCustomerHasDefaultAddress`.
  - `Seed_ProductsHaveCategories`: `>= 15` ürün, hepsinde `CategoryId != null`.
  - `Seed_HasCompetingListings`: `>= 4` ürün 2 ayrı satıcıda.
  - `Seed_HasLowAndOutOfStockListings`.
  - `Seed_HasDemoOrdersForBothSellers`: her satıcının `>= 3` SellerOrder'ı var.
  - `Seed_IsIdempotent`: `SeedAsync` iki kez çalıştırılınca Users, Products, Listings, Addresses ve Orders sayıları değişmiyor.
- [ ] **Step 2:** `dotnet test --filter DbSeederTests` → FAIL.
- [ ] **Step 3:** `DbSeeder.SeedAsync`'i yeniden yaz. Bölümleri ayrı private static metotlara böl: `SeedUsersAsync`, `SeedCategoriesAsync`, `SeedProductsAsync`, `SeedListingsAsync`, `SeedAddressesAsync`, `SeedOrdersAsync`.
- [ ] **Step 4:** `dotnet test Exodus.sln` → hepsi geçmeli.
- [ ] **Step 5:** Commit: `fix(seed): seller-owned listings, categories, addresses and demo orders`.

### Task 7: Konfigürasyon, user-secrets ve README

**Files:**
- Modify: `Exodus.csproj` (`<UserSecretsId>`), `appsettings.json:28,43-44`, `README.md`, `api-test.http`

- [ ] **Step 1:** `dotnet user-secrets init`. Ardından secret'ları ayarla: `ConnectionStrings:DefaultConnection` = `Server=127.0.0.1,1433;Database=Exodusdb;User Id=sa;Password=<scratchpad'deki şifre>;TrustServerCertificate=True`. Sonra scratchpad'deki şifre dosyasını sil.
- [ ] **Step 2:** `appsettings.json`: Email `BaseUrl` → `http://localhost:5013`. iyzico `ThreeDSCallbackUrl` → `http://localhost:5013/api/payment/3ds-callback`. `CallbackUrl` için `grep -rn "CallbackUrl" Services` ile kullanıldığı yeri bul ve gerçek route'a çevir (`/api/payment/gateway/3ds/complete` ya da `/api/payment/3ds-callback`); hiçbir yerde kullanılmıyorsa sil.
- [ ] **Step 3:** README:
  - Endpoint tablolarını koddaki route'larla eşle. Bilinen farklar: `/api/product`, `/api/order`, `/api/payment`, `/api/auth/refresh`, `GET /api/auth/verify-email?token=`, `/api/admin/gift-cards`, `/api/admin/taxes`. `/api/wishlist` satırını kaldır; yeni `/api/listings/search` satırını ekle.
  - "Local setup (macOS/Linux)" bölümü ekle: Docker SQL Server komutu, `dotnet user-secrets set ...`, `dotnet run --launch-profile http`, Swagger `http://localhost:5013/swagger`.
  - "Demo accounts" tablosu ve "Security fixes" bölümü ekle (Task 3–4 özetleri).
- [ ] **Step 4:** `api-test.http`: eski `/api/seller/{id}/orders` ve customer ile yapılan `simulate-success` örneklerini güncelle.
- [ ] **Step 5:** Commit: `docs: correct routes, local setup, demo accounts; fix callback URLs`.

### Task 8: Canlı uçtan uca doğrulama ve PR

- [ ] **Step 1:** Veritabanını sıfırla: `dotnet ef database drop -f` (gerekirse önce `dotnet tool install -g dotnet-ef`). Ardından `dotnet run --launch-profile http` (arka planda) ve `curl http://localhost:5013/health` → `200`.
- [ ] **Step 2:** `curl` senaryosu, her adımda beklenen kod:
  1. `ahmet@test.com` login.
  2. `GET /api/listings/search?q=kulaklık&sortBy=price` → `200`, `totalCount >= 1`.
  3. `GET /api/address` → varsayılan adres var.
  4. `POST /api/cart/add` (bir zeynep listing'i).
  5. `POST /api/order/checkout` → `200`.
  6. `POST /api/payment/intents` `CashOnDelivery` → `200`.
  7. Müşteri token'ıyla `simulate-success` → `403`.
  8. `zeynep@test.com` → `POST /api/seller/orders/{id}/confirm` → `200`.
  9. Zeynep → `POST /api/seller/orders/{id}/ship` `{Yurtici, TR123}` → `200`; ardından `GET /api/shipments/seller-orders/{id}` → `trackingNumber "TR123"`.
  10. `mehmet@test.com` aynı `GET` → `403`.
  11. `GET /api/seller/2/orders` → `404`.
- [ ] **Step 3:** Kullanıcı onayıyla: `git push -u origin chore/mcp-readiness` ve `gh pr create`. PR gövdesinde Task 3–6 özetleri ve güvenlik düzeltmeleri listesi yer alacak. CI yeşil olmalı.
