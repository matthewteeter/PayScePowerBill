# PayScePowerBill

## Purpose
Southern California Edison doesn't allow scheduled autopay when paying with credit card, but I want to get 5% cash back, so I pay with credit card.

## Click-Ops Payment Process
For the past 10 years, the payment site required typing various credit card details each time manually, clicking through many screens, etc.
This was addressed in November 2023, finally allowing saved payment cards. Still, to approach autopay-like convenience, we'd need to automate logging in, clicking through screens, etc. This program automates all that, allowing bill payment to be automated.

## Solution
This program controls a browser, navigates through SCE's website, and pays your bill using the first pre-saved credit card. If running locally, it automates a browser and you can see what it's doing. If running in Docker, it operates headlessly.
Schedule this program to run once a month (or more often, as it does nothing if 0 balance due), using Task Scheduler, cron, K8s CronJobs, etc.
Pass it parameters via command line, environment variable, or config file. 

### Features
* Optionally integrates with HCP Vault Secrets, a free SaaS secrets vault
* Configurable value to only pay bill if cash back would outweigh the credit card fee
* Runs as a native executable (requires .NET runtime) on Windows, Mac, Linux, or as a Docker container on Linux (235MB image)

## Usage
See the appsettings.json file for the settings which can be configured.
At minimum, you will need to provide your SCE username and password. This program assumes you have already saved a payment card to your account, to avoid handling CC numbers.

### Native executable
To install the browsers it needs, invoke it with a single command line argument, "install". It will install needed browsers and exit.
Running it without arguments makes it pay the bill.

### Docker
You will likely want to pass the parameters in as env vars, such as like this:
```docker run -e SceEmail=you@bla.com ScePassword=your_sce_pw synerynx/payscepowerbill```

### Store SCE account credentials in HCP Vault Secrets
To retrieve secrets from HCP Vault Secrets service, set the following config items and ensure your app's secrets are named SceEmail and ScePassword:
```docker run -e HCP_CLIENT_ID=<your_hcp_id> -e HCP_CLIENT_SECRET=<your_hcp_client_secret> -e HcpVaultSecrets__ProjectId=<vault_secrets_proj_id> -e HcpVaultSecrets__OrgId=<vault_secrets_org_id> -e HcpVaultSecrets__AppName=<vault_secrets_app> synerynx/payscepowerbill```
For more details on how this HCP Vault Secrets provider works, see the [config provider](https://github.com/matthewteeter/HcpVaultSecretsConfigProvider).

## Troubleshooting
The best way to troubleshoot is to run the local executable and watch what the browser is doing, as well as the command line output.
Enable debug logging for secret retrieval by uncommenting the ```Logging:LogLevel:HcpVaultSecretsConfigProvider.HcpVaultSecretsConfigurationProvider``` section.
