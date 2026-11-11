using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tripgo.Tests.E2E;

/// <summary>
/// Real-browser journeys against a running application. They run only when TRIPGO_E2E_BASE_URL is set,
/// and use the machine's installed Edge. See docs/local-development.md. They are reported as skipped,
/// never as passed, when the application is not available.
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRIPGO_E2E_BASE_URL")))
        {
            Skip = "Set TRIPGO_E2E_BASE_URL to a running TripGo instance to run browser tests.";
        }
    }
}

public sealed class BrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRIPGO_E2E_BASE_URL")))
        {
            return;
        }

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser != null)
        {
            await Browser.CloseAsync();
        }

        Playwright?.Dispose();
    }
}

public sealed class BrowserJourneyTests : IClassFixture<BrowserFixture>
{
    private static readonly string BaseUrl = (Environment.GetEnvironmentVariable("TRIPGO_E2E_BASE_URL") ?? string.Empty).TrimEnd('/');
    private readonly BrowserFixture _fixture;

    public BrowserJourneyTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<IPage> NewCustomerAsync(int width = 1280, int height = 900)
    {
        var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(30_000);
        return page;
    }

    /// <summary>Waits for the Blazor circuit, because clicks on prerendered markup do nothing.</summary>
    private static async Task GotoInteractiveAsync(IPage page, string path)
    {
        await page.GotoAsync(BaseUrl + path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined");
        await page.WaitForTimeoutAsync(800);
    }

    private static async Task OpenTripAsync(IPage page)
    {
        await GotoInteractiveAsync(page, "/");
        var departureDate = page.Locator(".tg-search-box input[type='date']");
        var searchBox = page.Locator(".tg-search-box");
        var initialDate = await departureDate.InputValueAsync();
        await searchBox.GetByRole(AriaRole.Button, new() { Name = "Ngày mai", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(
            "previousDate => document.querySelector(\".tg-search-box input[type='date']\")?.value !== previousDate",
            initialDate);
        await searchBox.GetByRole(AriaRole.Button, new() { Name = "Tìm vé" }).ClickAsync();
        var firstDepartureDate = DateOnly.ParseExact(
            await departureDate.InputValueAsync(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        for (var dayOffset = 1; dayOffset <= 14; dayOffset++)
        {
            if (dayOffset > 1)
            {
                await departureDate.FillAsync(firstDepartureDate.AddDays(dayOffset - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                await searchBox.GetByRole(AriaRole.Button, new() { Name = "Tìm vé" }).ClickAsync();
            }

            await page.Locator("#trip-results").GetByText("KẾT QUẢ TÌM KIẾM").WaitForAsync();
            var cards = page.Locator(".trip-card");
            ILocator? selectedTrip = null;
            for (var index = 0; index < await cards.CountAsync(); index++)
            {
                var card = cards.Nth(index);
                var summary = await card.InnerTextAsync();
                var capacity = Regex.Match(summary, @"Còn\s+(\d+)\s*/");
                if (!summary.Contains("[SANDBOX]-", StringComparison.Ordinal) ||
                    !capacity.Success ||
                    !int.TryParse(capacity.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var availableSeats) ||
                    availableSeats < 2)
                {
                    continue;
                }

                selectedTrip = card;
                break;
            }

            if (selectedTrip == null)
            {
                continue;
            }

            await selectedTrip.GetByRole(AriaRole.Button, new() { Name = "Chọn chuyến này" }).ClickAsync();
            try
            {
                await page.Locator("button.tg-seat-berth.seat-available").First.WaitForAsync();
                return;
            }
            catch (TimeoutException)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Quay lại danh sách chuyến" }).ClickAsync();
            }
        }

        throw new InvalidOperationException("No sandbox trip with at least two available seats was found in the next 14 days.");
    }

    private static async Task<string> SelectFreeSeatAsync(IPage page, int index = 0)
    {
        var seat = page.Locator("button.tg-seat-berth.seat-available").Nth(index);
        var code = (await seat.Locator(".tg-berth-code").InnerTextAsync()).Trim();
        await seat.ClickAsync();
        return code;
    }

    private static async Task ContinueToPassengerAsync(IPage page)
    {
        var continueButton = page.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Điểm đón/trả" });
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('button')].some(button => button.textContent.includes('Tiếp tục: Điểm đón/trả') && !button.disabled)");
        await continueButton.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Thông tin khách" }).ClickAsync();
    }

    private static async Task FillPassengerAsync(IPage page, string email = "e2e.khach@tripgo.test")
    {
        await page.GetByPlaceholder("Ví dụ: Nguyễn Văn A").FillAsync("Nguyễn Văn Khách");
        await page.GetByPlaceholder("Ví dụ: 0912345678").FillAsync("0912345678");
        await page.GetByPlaceholder("example@gmail.com").FillAsync(email);
        await page.Locator("#contactEmailConfirmed").CheckAsync();
    }

    private static Task ContinueToPaymentAsync(IPage page) =>
        page.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Thanh toán" }).ClickAsync();

    [E2EFact]
    public async Task SuccessfulBooking_ReachesConfirmation_AndAppearsInMyTickets_WithASandboxCode()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();

        await page.GetByText("Đặt chỗ và thanh toán mô phỏng hoàn tất").WaitForAsync();
        var code = (await page.Locator(".tg-boarding-pass strong.font-monospace").First.InnerTextAsync()).Trim();
        Assert.StartsWith("TRIPGO-", code);

        await page.GetByRole(AriaRole.Link, new() { Name = "Xem thông tin đặt chỗ" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog).WaitForAsync();
        await page.GetByText("Mã kiểm tra SANDBOX").WaitForAsync();
        Assert.True(await page.Locator(".tg-ticket-qr-image svg").IsVisibleAsync());
        Assert.Contains("SANDBOX", await page.Locator(".tg-ticket-qr-stamp").InnerTextAsync());
    }

    [E2EFact]
    public async Task InvalidPassengerData_StopsAtThePassengerStep()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page, email: "not-an-email");
        await page.Locator("#contactEmailConfirmed").CheckAsync();
        await ContinueToPaymentAsync(page);

        await page.GetByText("Vui lòng nhập email liên hệ hợp lệ").WaitForAsync();
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).CountAsync());
    }

    [E2EFact]
    public async Task MultiSeatBooking_RequiresACompanionName()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page, 0);
        await SelectFreeSeatAsync(page, 0);
        await page.GetByText("Ghế đã chọn (2):").WaitForAsync();
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);

        await page.GetByText("Vui lòng nhập họ tên hành khách cho ghế").WaitForAsync();
    }

    [E2EFact]
    public async Task PaymentFailure_KeepsTheOrder_LocksItsSeats_AndCanBeRetried()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);
        await page.GetByText("Thất bại (Từ chối thẻ / Hết hạn)").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();

        await page.GetByText("Thanh toán sandbox chưa được xác nhận").WaitForAsync();

        // The order exists now: going back to change seats would pay for the wrong thing, so that path is gone.
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Quay lại thông tin khách" }).CountAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "và chọn lại" }).IsVisibleAsync());

        await page.GetByText("Thành công (Xác nhận đơn & cấp vé)").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();
        await page.GetByText("Đặt chỗ và thanh toán mô phỏng hoàn tất").WaitForAsync();
    }

    [E2EFact]
    public async Task CancellingAFailedOrder_ReturnsToSeatSelection_WithTheSeatFreeAgain()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        var seat = await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);
        await page.GetByText("Thất bại (Từ chối thẻ / Hết hạn)").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "và chọn lại" }).ClickAsync();

        await page.GetByText("Vui lòng chọn lại ghế").WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = $"Ghế {seat}, còn trống" }).IsEnabledAsync());
    }

    [E2EFact]
    public async Task ConcurrentSeatSelection_OnlyOneCustomerGetsTheSeat()
    {
        var first = await NewCustomerAsync();
        var second = await NewCustomerAsync();
        await OpenTripAsync(first);
        await OpenTripAsync(second);

        var seat = await SelectFreeSeatAsync(first);
        await second.GetByRole(AriaRole.Button, new() { Name = $"Ghế {seat}, còn trống" }).ClickAsync();

        await first.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Điểm đón/trả" }).ClickAsync();
        await first.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Thông tin khách" }).WaitForAsync();
        await second.GetByRole(AriaRole.Button, new() { Name = "Tiếp tục: Điểm đón/trả" }).ClickAsync();

        await second.GetByText("đang được giữ chỗ bởi hành khách khác").WaitForAsync();
        await second.GetByRole(AriaRole.Button, new() { Name = $"Ghế {seat}, đang được giữ" }).WaitForAsync();
        Assert.False(await second.GetByRole(AriaRole.Button, new() { Name = $"Ghế {seat}, đang được giữ" }).IsEnabledAsync());
    }

    [E2EFact]
    public async Task ReloadingDuringCheckout_LeavesTheOrderRecoverable_InMyTickets()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);
        await page.GetByText("Thất bại (Từ chối thẻ / Hết hạn)").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();
        await page.GetByText("Thanh toán sandbox chưa được xác nhận").WaitForAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await GotoInteractiveAsync(page, "/my-tickets");
        await page.GetByText("THANH TOÁN THẤT BẠI - CÓ THỂ THỬ LẠI").First.WaitForAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Xem thông tin đặt chỗ" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán mô phỏng" }).First.ClickAsync();
        await page.GetByText("Đã thanh toán sandbox đơn").WaitForAsync();
    }

    [E2EFact]
    public async Task Cancellation_FromMyTickets_ChangesItsStatus()
    {
        var page = await NewCustomerAsync();
        await OpenTripAsync(page);
        await SelectFreeSeatAsync(page);
        await ContinueToPassengerAsync(page);
        await FillPassengerAsync(page);
        await ContinueToPaymentAsync(page);
        await page.GetByText("Thất bại (Từ chối thẻ / Hết hạn)").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();
        await page.GetByText("Thanh toán sandbox chưa được xác nhận").WaitForAsync();

        await GotoInteractiveAsync(page, "/my-tickets");
        await page.GetByRole(AriaRole.Button, new() { Name = "Hủy đặt chỗ" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Xác nhận hủy đơn" }).ClickAsync();

        await page.GetByText("Ghế đã được hoàn trả lại hệ thống.").WaitForAsync();
    }

    [E2EFact]
    public async Task AnotherSession_CannotLookUpSomeoneElsesBooking()
    {
        var owner = await NewCustomerAsync();
        await OpenTripAsync(owner);
        await SelectFreeSeatAsync(owner);
        await ContinueToPassengerAsync(owner);
        await FillPassengerAsync(owner);
        await ContinueToPaymentAsync(owner);
        await owner.GetByRole(AriaRole.Button, new() { Name = "Thanh toán sandbox" }).ClickAsync();
        await owner.GetByText("Đặt chỗ và thanh toán mô phỏng hoàn tất").WaitForAsync();
        var code = (await owner.Locator(".tg-boarding-pass strong.font-monospace").First.InnerTextAsync()).Trim();

        var stranger = await NewCustomerAsync();
        await GotoInteractiveAsync(stranger, $"/my-tickets?code={code}");

        await stranger.GetByText("Không tìm thấy vé phù hợp hoặc bạn không có quyền xem vé này.").WaitForAsync();
        Assert.Equal(0, await stranger.GetByRole(AriaRole.Dialog).CountAsync());
    }

    [E2EFact]
    public async Task AdminPages_RejectAnonymousVisitors()
    {
        var page = await NewCustomerAsync();

        foreach (var path in new[] { "/admin", "/admin/bookings", "/admin/trips", "/staff/verify-ticket" })
        {
            await page.GotoAsync(BaseUrl + path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Contains("/login", page.Url);
        }
    }

    [E2EFact]
    public async Task Admin_CanSignIn_AndOpenEveryManagementPage_WithoutErrors()
    {
        var password = Environment.GetEnvironmentVariable("TRIPGO_SANDBOX_ADMIN_PASSWORD") ?? "AdminPassword123!";

        var page = await NewCustomerAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await GotoInteractiveAsync(page, "/login?returnUrl=/admin");
        await page.Locator("#loginIdentifier").FillAsync("admin@tripgo.test");
        await page.Locator("#loginPassword").FillAsync(password!);
        await page.Locator("button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/admin");

        var pages = new Dictionary<string, string>
        {
            ["/admin"] = "Tổng quan",
            ["/admin/operators"] = "Nhà xe",
            ["/admin/routes"] = "Tuyến đường",
            ["/admin/fleet"] = "Đội xe và tài xế",
            ["/admin/trips"] = "Chuyến xe",
            ["/admin/bookings"] = "Đơn đặt chỗ",
            ["/admin/monitoring"] = "Giám sát",
            ["/staff/verify-ticket"] = "Kiểm tra mã vé"
        };

        foreach (var (path, heading) in pages)
        {
            await GotoInteractiveAsync(page, path);
            await page.GetByRole(AriaRole.Heading, new() { Name = heading, Level = 1 }).WaitForAsync();
            Assert.Equal(0, await page.Locator(".adm-status.is-error").CountAsync());
        }

        Assert.Empty(errors);
    }

    [E2EFact]
    public async Task EveryPublicPage_FitsAPhoneScreen_WithoutSidewaysScrolling()
    {
        var page = await NewCustomerAsync(375, 812);

        foreach (var path in new[] { "/", "/schedules", "/my-tickets", "/support", "/login", "/register", "/forgot-password" })
        {
            await page.GotoAsync(BaseUrl + path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
            Assert.True(overflow <= 0, $"{path} overflows by {overflow}px at phone width.");
        }
    }
}
