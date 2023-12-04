using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;
using HcpVaultSecretsConfigProvider;

using var playwright = await Playwright.CreateAsync();
var b = playwright.Firefox;
if (args.Any() && args?[0] == "install")
{
    Environment.Exit(Microsoft.Playwright.Program.Main(new[] { "install", b.Name }));
    //Environment.Exit(Microsoft.Playwright.Program.Main(new[] { "install-deps", b.Name }));
}
bool inDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
Console.WriteLine(!inDocker ? "Starting SCE payment program..." : "Starting SCE payment program in headless mode...");
using IHost host = Host.CreateDefaultBuilder(args)
                       .UseEnvironment("Development") //enable user secrets in Development for local overrides
                       .ConfigureAppConfiguration(config => config.AddHcpVaultSecretsConfiguration(config.Build())).Build(); 
// if running locally, you can set the parameters using dotnet user-secrets. If docker, pass in via Env Vars.
IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

await using var browser = await b.LaunchAsync(new BrowserTypeLaunchOptions { Headless = inDocker });
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();

await LoginToSce(config["SceEmail"] ?? string.Empty, config["ScePassword"] ?? string.Empty, page);
await ExitIfZeroBalanceDueOrLessThanMinimum(page, config["DontPayIfUnder"] ?? "1"); //check balance to avoid using CC if fee outweighs cashback
var page1 = await page.RunAndWaitForPopupAsync(async () =>
{
    await page.GetByRole(AriaRole.Link, new() { Name = "More Ways to Pay" }).ClickAsync();
});
await page1.GetByRole(AriaRole.Button, new() { Name = "Pay with card" }).ClickAsync();
await SelectAccount(page1);
await SelectPaymentMethod(page1);
//await AddPaymentMethod(page1, config["CardNum"] ?? string.Empty, config["Code"] ?? string.Empty,
//                        config["ExpiryMonth"] ?? string.Empty, config["ExpiryYear"] ?? string.Empty, 
//                        config["CardholderName"] ?? string.Empty, config["Zip"] ?? string.Empty);
await ReviewPaymentAndConfirm(page1);
Console.WriteLine("Finished payment program.");

static async Task ExitIfZeroBalanceDueOrLessThanMinimum(IPage page, string dontPayIfUnder = "")
{
    string rawBalance = await page.GetByText("Balance Due:").InnerTextAsync();
    decimal balance = decimal.MinusOne;
    if (!string.IsNullOrWhiteSpace(rawBalance))
    {
        Console.WriteLine($"Found balance: {rawBalance}");
        string unparsedBalance = rawBalance.Split(":")[1]?.Replace("$", string.Empty);
        if (string.IsNullOrWhiteSpace(unparsedBalance))
        {
            Console.WriteLine("Unable to parse rawBalance. Does it not contain a colon?");
        }
        else
        {
            Console.WriteLine($"Parsing {unparsedBalance}");
            balance = decimal.Parse(unparsedBalance);
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

    await page.GetByPlaceholder("User ID/Email").ClickAsync();
    await page.GetByPlaceholder("User ID/Email").FillAsync(username);

    await page.GetByRole(AriaRole.Textbox, new() { Name = "password" }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "password" }).FillAsync(password);

    await page.GetByRole(AriaRole.Button, new() { Name = "Log In", Exact = true }).ClickAsync();
    Console.WriteLine($"Logging into SCE as {username}...");
    await Task.Delay(8000);
}

async Task SelectAccount(IPage page)
{
    //For now, assume the user only has 1 residence and 1 power bill account. Pick the only option.
    await page.GetByText("RESIDENTIAL PAYMENT #").ClickAsync();
    Console.WriteLine("Selecting account (assuming the first one)...");
    await page.GetByLabel("Continue to provide payment").ClickAsync();
    Console.WriteLine("Clicking continue to payment...");
    await Task.Delay(8000); // it loads this with ajax so need to wait
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
//TODO: remove this after testing confirms we don't need it
async Task AddPaymentMethod(IPage page, string cardNum, string cvv, string month, string year, string name, string zip)
{
    await page.GetByRole(AriaRole.Link, new() { Name = "Add Payment method" }).ClickAsync();
    await page.GetByRole(AriaRole.Link, new() { Name = "Credit" }).ClickAsync();

    await page.GetByRole(AriaRole.Textbox, new() { Name = "Card Number" }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Card Number" }).FillAsync(cardNum);

    await page.GetByRole(AriaRole.Textbox, new() { Name = "CVV " }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "CVV " }).FillAsync(cvv);

    await page.GetByRole(AriaRole.Group, new() { Name = "Expiration Date" }).GetByLabel("Month").SelectOptionAsync(new[] { month });
    await page.GetByRole(AriaRole.Group, new() { Name = "Expiration Date" }).GetByLabel("Year").SelectOptionAsync(new[] { year });

    await page.GetByRole(AriaRole.Textbox, new() { Name = "Card Holder Name" }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "Card Holder Name" }).FillAsync(name);

    await page.GetByRole(AriaRole.Textbox, new() { Name = "ZIP Code" }).ClickAsync();
    await page.GetByRole(AriaRole.Textbox, new() { Name = "ZIP Code" }).FillAsync(zip);

    await page.GetByText("I authorize payment and agree").First.ClickAsync();

    await page.GetByRole(AriaRole.Link, new() { Name = "Add", Exact = true }).ClickAsync();
}

async Task ReviewPaymentAndConfirm(IPage page)
{
    await page.GetByLabel("Continue to confirm payment").ClickAsync();
    Console.WriteLine("Clicking continue...");
    await Task.Delay(5000);
    Console.WriteLine("Clicking Pay...");
    await page.GetByRole(AriaRole.Link, new() { Name = "Pay $" }).ClickAsync();
    await Task.Delay(8000);
}