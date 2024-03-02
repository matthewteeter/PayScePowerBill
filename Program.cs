using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using System;
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

await using var browser = await b.LaunchAsync(new BrowserTypeLaunchOptions { Headless = inDocker });
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();

await LoginToSce(config["SceEmail"] ?? string.Empty, config["ScePassword"] ?? string.Empty, page);
await ExitIfZeroBalanceDueOrLessThanMinimum(page, config["DontPayIfUnder"] ?? "1"); //check balance to avoid using CC if fee outweighs cashback

await page.GetByRole(AriaRole.Link, new() { Name = "Do Not Show Me Again" }).ClickAsync();//this popup may go away in the future
await page.GetByRole(AriaRole.Button, new() { Name = "Make a Payment" }).ClickAsync();
await page.GetByRole(AriaRole.Button, new() { Name = "Pay by Card" }).ClickAsync();
await SelectAccount(page);
await SelectPaymentMethod(page);
await ReviewPaymentAndConfirm(page);
Console.WriteLine("Finished payment program.");

static async Task ExitIfZeroBalanceDueOrLessThanMinimum(IPage page, string dontPayIfUnder = "")
{
    string rawHomepageText = await page.Locator("#your_account_balance").InnerTextAsync();
    string pattern = @"Amount Due on \D\D\D\s\d\d\s*\$(\d+?\.\d\d)"; //expect 1 match and 1 group
    RegexOptions options = RegexOptions.Multiline;
    Match m = Regex.Matches(rawHomepageText, pattern, options).FirstOrDefault();
    if(m?.Groups?.Count != 2)
    {
        Console.WriteLine($"Unable to parse homepage for balance due: {rawHomepageText}");
        Environment.Exit(1);
    }
    string rawBalance = m.Groups[1].Value;
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
    await page.GetByRole(AriaRole.Link, new() { Name = "Continue" }).ClickAsync();
    Console.WriteLine("Clicking continue...");
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

async Task ReviewPaymentAndConfirm(IPage page)
{
    await page.GetByRole(AriaRole.Link, new() { Name = "Continue" }).Nth(1).ClickAsync();
    Console.WriteLine("Clicking continue...");
    await Task.Delay(5000);
    Console.WriteLine("Clicking Pay...");
    await page.GetByRole(AriaRole.Link, new() { Name = "Pay $" }).ClickAsync();
    await Task.Delay(8000);
}