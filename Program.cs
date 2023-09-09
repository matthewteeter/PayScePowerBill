using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

Console.WriteLine("Starting SCE payment program...");
using IHost host = Host.CreateDefaultBuilder(args).UseEnvironment("Development").Build(); //enable user secrets in Development
// if running locally, you can set the parameters using dotnet user-secrets. If docker, pass in via Env Vars.
IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false, });
var context = await browser.NewContextAsync();

var page = await context.NewPageAsync();

await page.GotoAsync("https://www.sce.com/");

await page.Locator(".sceTextBox__sceFormContainer__VMuuU").First.ClickAsync();

await page.GetByLabel("userName").FillAsync(config["SceEmail"]);

await page.GetByLabel("userName").PressAsync("Tab");

//await page.GetByText("ErrorPassword hint: at least 8 characters, 1 number, 1 uppercase,and 1 lowercase").PressAsync("Shift+Tab");

await page.GetByRole(AriaRole.Button, new() { Name = "Show" }).PressAsync("Shift+Tab");

await page.GetByLabel("password", new() { Exact = true }).ClickAsync();

await page.GetByLabel("password", new() { Exact = true }).FillAsync(config["ScePassword"]);

await page.GetByLabel("password", new() { Exact = true }).PressAsync("Enter");

await Task.Delay(10000);

//check balance - if zero exit successfully
await ExitIfZeroBalanceDueOrLessThanMinimum(page, config["DontPayIfUnder"]);

//await page.GotoAsync("https://www.sce.com/");

await page.GotoAsync("https://www.sce.com/mysce/myaccount");

var page1 = await page.RunAndWaitForPopupAsync(async () =>
{
    await page.GetByRole(AriaRole.Link, new() { Name = "More Ways to Pay" }).ClickAsync();
});

var page2 = await page1.RunAndWaitForPopupAsync(async () =>
{
    await page1.GetByRole(AriaRole.Link, new() { Name = "Pay with Debit/Credit Card" }).ClickAsync();
});

await page2.GetByLabel("Southern California Edison Account Number*:").FillAsync(config["SceAccountNum"]);

await page2.GetByLabel("Southern California Edison Account Number*:").PressAsync("Tab");

await page2.GetByLabel("Web Password*:").FillAsync(config["Zip"]);

await page2.GetByLabel("Web Password*:").PressAsync("Enter");

await Task.Delay(15000);

await page2.GetByLabel("Email Address*:").ClickAsync();

await page2.GetByLabel("Email Address*:").FillAsync(config["SceEmail"]);

await page2.GetByLabel("Email Address*:").PressAsync("Enter");

//await page2.Locator("#autoSavedAccountId").ClickAsync();

//await page2.Locator("#savedAccountSection div").Filter(new() { HasText = "Saved Account*:" }).ClickAsync();

await page2.GetByLabel("Credit/Debit/ATM Card\n        \n        \n          Debit Card\n        \n        \n          Debit/ATM Card\n        \n        \n          Credit/Debit Card\n        \n        \n          ATM Card").CheckAsync();

await page2.Locator("#ajaxcreditcardMasked").ClickAsync();

await page2.Locator("#ajaxcreditcardMasked").FillAsync(config["CardNum"]);

await page2.Locator(".linecompressedindent > div:nth-child(2)").ClickAsync();

await page2.GetByRole(AriaRole.Textbox, new() { Name = "Cardholder Name*:" }).ClickAsync();

await page2.GetByRole(AriaRole.Textbox, new() { Name = "Cardholder Name*:" }).FillAsync(config["CardholderName"]);

await page2.GetByRole(AriaRole.Textbox, new() { Name = "Cardholder Name*:" }).PressAsync("Tab");

await page2.GetByTitle("Expiration Date Month").SelectOptionAsync(new[] { (int.Parse(config["ExpiryMonth"]) - 1).ToString() });

await page2.GetByTitle("Expiration Date Year").SelectOptionAsync(new[] { config["ExpiryYear"] });

await page2.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
//can't test this yet.
await page2.Locator("#cvd").ClickAsync();

await page2.Locator("#cvd").FillAsync(config["Code"]);

await page2.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync();

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