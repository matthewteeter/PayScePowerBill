using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Threading.Tasks;
using HcpVaultSecretsConfigProvider;
using System.Text.RegularExpressions;

using var playwright = await Playwright.CreateAsync();
var b = playwright.Firefox;
if (args.Any() && args?[0] == "install")
{
    Environment.Exit(Microsoft.Playwright.Program.Main(new[] { "install", b.Name }));
}
bool inDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
Console.WriteLine(!inDocker ? "Starting SCE payment program..." : "Starting SCE payment program in headless mode...");
using IHost host = Host.CreateDefaultBuilder(args)
                       .UseEnvironment("Development") //enable user secrets in Development for local overrides
                       .ConfigureAppConfiguration(config => config.AddHcpVaultSecretsConfiguration(config.Build())).Build(); 
// if running locally, you can set the parameters using dotnet user-secrets. If docker, pass in via Env Vars.
IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

const string windowsUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0";
var browserSetup = await LaunchBrowserContextWithPersistenceAsync(
    b,
    inDocker,
    windowsUserAgent,
    config["PlaywrightUserDataDir"]);
await using var browser = browserSetup.Browser;
await using var context = browserSetup.Context;
var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

await LoginToSce(config["SceEmail"] ?? string.Empty, config["ScePassword"] ?? string.Empty, page);
await ExitIfZeroBalanceDueOrLessThanMinimum(page, config["DontPayIfUnder"] ?? "1"); //check balance to avoid using CC if fee outweighs cashback
await DismissModalIfFound(page);

await ClickWithMousePath(page, page.GetByRole(AriaRole.Button, new() { Name = "Make a Payment" }));
await ClickWithMousePath(page, page.GetByRole(AriaRole.Button, new() { Name = "Pay by Card" }));
await PauseBetweenActionsAsync(14000, 28000); //let the payment page load
await SelectAccount(page);
await SelectPaymentMethod(page);
await ReviewPaymentAndConfirm(page);
Console.WriteLine("Finished payment program.");

async Task DismissModalIfFound(IPage page)
{
    Console.Write("Checking for modal dialog...");
    ILocator buttonLocator = page.Locator("a#DSSdismiss");
    bool isButtonVisible = await buttonLocator.IsVisibleAsync();
    Console.WriteLine(isButtonVisible);
    if(isButtonVisible)
    {
        Console.WriteLine("Clicking Close on modal...");
        await ClickWithMousePath(page, buttonLocator);
    }
}

static async Task ExitIfZeroBalanceDueOrLessThanMinimum(IPage page, string dontPayIfUnder = "")
{
    string rawHomepageText = await page.Locator("#your_account_balance").InnerTextAsync();
    string pattern = @"(Amount Due on \D\D\D\s\d\d\s*)?\$(\d+?\.\d\d)"; //expect 1 match and 2 groups
    RegexOptions options = RegexOptions.Multiline;
    Match m = Regex.Matches(rawHomepageText, pattern, options).FirstOrDefault();
    if(m?.Groups?.Count != 3)
    {
        Console.WriteLine($"Unable to parse homepage for balance due: {rawHomepageText}");
        Environment.Exit(1);
    }
    string rawBalance = m.Groups[2].Value;
    decimal balance = decimal.MinusOne;
    if (!string.IsNullOrWhiteSpace(rawBalance))
    {
        Console.WriteLine($"Found balance: {rawBalance}");
        balance = decimal.Parse(rawBalance);
        Console.WriteLine($"Parsed {balance}");
        if (balance <= 0)
        {
            Console.WriteLine("Zero balance due, exiting with success!");
            Environment.Exit(0);
        }
        else if (!string.IsNullOrWhiteSpace(dontPayIfUnder))
        {
            decimal dontPayIfUnderParsed = decimal.Parse(dontPayIfUnder);
            Console.WriteLine($"Checking if balance due is under minimum of {dontPayIfUnderParsed}...");
            if(balance < dontPayIfUnderParsed)
            {
                Console.WriteLine($"Aborting payment because it is less than the minimum of {dontPayIfUnderParsed}. This check is in place to ensure the cashback gained outweighs the credit card fee.");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Proceeding to pay...");
            }
        }
    }
    else
    {
        Console.WriteLine("Unable to determine balance due.");
    }
}

static async Task LoginToSce(string username, string password, IPage page)
{
    Console.WriteLine("Preparing to log into SCE.com...");
    if(string.IsNullOrWhiteSpace(username))
    {
        throw new ApplicationException("Can't log in to SCE.com because SceEmail was empty!");
    }
    if (string.IsNullOrWhiteSpace(password))
    {
        throw new ApplicationException("Can't log in to SCE.com because ScePassword was empty!");
    }
    await page.GotoAsync("https://www.sce.com/mysce/myaccount");

    ILocator emailField = page.GetByPlaceholder("Email address");
    await TypeTextLikeHuman(page, emailField, username);

    ILocator passwordField = page.GetByRole(AriaRole.Textbox, new() { Name = "password" });
    await TypeTextLikeHuman(page, passwordField, password);

    await ClickWithMousePath(page, page.GetByRole(AriaRole.Button, new() { Name = "LOG IN", Exact = true }));
    Console.WriteLine($"Logging into SCE as {username}...");
    await PauseBetweenActionsAsync(12000, 24000);
}

static async Task TypeTextLikeHuman(IPage page, ILocator field, string value)
{
    await ClickWithMousePath(page, field);
    await PauseBetweenActionsAsync(250, 500);
    await field.PressAsync("Control+A");
    await PauseBetweenActionsAsync(120, 240);
    await field.PressAsync("Backspace");
    await PauseBetweenActionsAsync(180, 360);

    foreach (char c in value)
    {
        await field.PressSequentiallyAsync(c.ToString());
        await PauseBetweenActionsAsync(90, 180);
    }
    await PauseBetweenActionsAsync(280, 560);
}

static async Task ClickWithMousePath(IPage page, ILocator target)
{
    await PauseBetweenActionsAsync(280, 560);
    await ScrollIntoViewLikeHuman(page, target);
    await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

    LocatorBoundingBoxResult? box = await target.BoundingBoxAsync();
    if (box is null)
    {
        await target.ClickAsync();
        await PauseBetweenActionsAsync(450, 900);
        return;
    }

    int viewportWidth = page.ViewportSize?.Width ?? 1366;
    int viewportHeight = page.ViewportSize?.Height ?? 768;
    const double edgePadding = 8;

    float targetX = (float)Math.Clamp(box.X + (box.Width / 2.0), edgePadding, viewportWidth - edgePadding);
    float targetY = (float)Math.Clamp(box.Y + (box.Height / 2.0), edgePadding, viewportHeight - edgePadding);

    int waypoints = Random.Shared.Next(2, 5);
    for (int i = 0; i < waypoints; i++)
    {
        float waypointX = (float)Math.Clamp(targetX + (Random.Shared.NextDouble() * 420) - 210, edgePadding, viewportWidth - edgePadding);
        float waypointY = (float)Math.Clamp(targetY + (Random.Shared.NextDouble() * 320) - 160, edgePadding, viewportHeight - edgePadding);

        await page.Mouse.MoveAsync(waypointX, waypointY, new MouseMoveOptions { Steps = Random.Shared.Next(12, 31) });
        await PauseBetweenActionsAsync(70, 140);
    }

    await page.Mouse.MoveAsync(targetX, targetY, new MouseMoveOptions { Steps = Random.Shared.Next(24, 61) });
    await PauseBetweenActionsAsync(130, 260);
    await page.Mouse.ClickAsync(targetX, targetY, new MouseClickOptions { Delay = Random.Shared.Next(25, 51) });
    await PauseBetweenActionsAsync(450, 900);
}

static async Task ScrollIntoViewLikeHuman(IPage page, ILocator target)
{
    await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });

    int viewportHeight = page.ViewportSize?.Height ?? 768;
    float upperBand = viewportHeight * 0.15f;
    float lowerBand = viewportHeight * 0.85f;

    for (int i = 0; i < 80; i++)
    {
        LocatorBoundingBoxResult? box = await target.BoundingBoxAsync();
        if (box is not null)
        {
            float centerY = (float)(box.Y + (box.Height / 2.0));
            if (centerY >= upperBand && centerY <= lowerBand)
            {
                return;
            }

            float direction = centerY < upperBand ? -1f : 1f;
            float deltaY = direction * Random.Shared.Next(30, 86);
            await page.Mouse.WheelAsync(0, deltaY);
            await PauseBetweenActionsAsync(140, 280);
            continue;
        }

        // If no box yet, nudge downward slowly until layout catches up.
        await page.Mouse.WheelAsync(0, Random.Shared.Next(30, 76));
        await PauseBetweenActionsAsync(160, 320);
    }

    // Last-resort fallback in case the page structure prevents wheel scrolling to this target.
    await target.ScrollIntoViewIfNeededAsync();
    await PauseBetweenActionsAsync(280, 560);
}

static Task PauseBetweenActionsAsync(int minMs = 350, int maxMs = 700)
{
    return Task.Delay(Random.Shared.Next(minMs, maxMs + 1));
}

static async Task<(IBrowserContext Context, IBrowser? Browser)> LaunchBrowserContextWithPersistenceAsync(
    IBrowserType browserType,
    bool headless,
    string userAgent,
    string? configuredUserDataDir)
{
    string? userDataDir = ResolvePersistentProfilePath(configuredUserDataDir);
    if (!string.IsNullOrWhiteSpace(userDataDir))
    {
        try
        {
            Directory.CreateDirectory(userDataDir);
            Console.WriteLine($"Using persistent browser profile: {userDataDir}");
            IBrowserContext persistentContext = await browserType.LaunchPersistentContextAsync(
                userDataDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = headless,
                    UserAgent = userAgent
                });

            return (persistentContext, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Persistent profile unavailable ({ex.GetType().Name}); falling back to non-persistent context.");
        }
    }
    else
    {
        Console.WriteLine("No persistent profile path available; using non-persistent context.");
    }

    IBrowser browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });
    IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = userAgent
    });
    return (context, browser);
}

static string? ResolvePersistentProfilePath(string? configuredUserDataDir)
{
    if (!string.IsNullOrWhiteSpace(configuredUserDataDir))
    {
        return configuredUserDataDir;
    }

    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (!string.IsNullOrWhiteSpace(localAppData))
    {
        return Path.Combine(localAppData, "PayPowerBill", "playwright-profile");
    }

    return null;
}

async Task SelectAccount(IPage page)
{
    //For now, assume the user only has 1 residence and 1 power bill account. Pick the only option.
    await ClickWithMousePath(page, page.GetByText("RESIDENTIAL PAYMENT #"));
    Console.WriteLine("Selecting account (assuming the first one)...");
    await ClickWithMousePath(page, page.GetByRole(AriaRole.Link, new() { Name = "Continue" }));
    Console.WriteLine("Clicking continue...");
    await PauseBetweenActionsAsync(13000, 26000); // it loads this with ajax so need to wait
}

async Task SelectPaymentMethod(IPage page)
{
    Console.WriteLine("Checking payment card is available...");
    ILocator cardLocator = page.GetByText("| Exp");
    int numCards = await cardLocator.CountAsync();
    if (numCards < 1)
    {
        throw new ApplicationException("No saved payment cards found in Wallet. Please add one to your account before running this program.");
    }
    //assume we want to pay with the first payment method that is selected by default
}

async Task ReviewPaymentAndConfirm(IPage page)
{
    await ClickWithMousePath(page, page.GetByRole(AriaRole.Link, new() { Name = "Continue" }));
    Console.WriteLine("Clicking continue...");
    await PauseBetweenActionsAsync(10000, 20000);
    Console.WriteLine("Clicking Pay...");
    await ClickWithMousePath(page, page.GetByRole(AriaRole.Link, new() { Name = "Pay $" }));
    await PauseBetweenActionsAsync(10000, 20000);
}
