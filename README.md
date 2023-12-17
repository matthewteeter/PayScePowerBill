#PayScePowerBill

## Purpose
Southern California Edison doesn't allow scheduled autopay when paying with credit card, but I want to get 5% cash back, so I pay with credit card.

## Click-Ops Payment Process
For the past 10 years, the payment site required typing various credit card details each time manually, clicking through many screens, etc.
This was addressed in November 2023, finally allowing saved payment cards. Still, the need to automate logging in, clicking through screens etc is still there to approach an autopay-like convenience.

## Solution
Schedule this program to run once a month (or more often, as it does nothing if 0 balance due), and pass it parameters via command line, environment variable, or config file. 

### Features
* Optionally integrates with HCP Vault Secrets, a free SaaS secrets vault
* Configurable value to only pay bill if cash back would outweigh the credit card fee
* Runs as a native executable on Windows, Mac, Linux, or as a Docker container on Linux (235MB image)

## Usage
See the appsettings.json file for the settings which can be configured.
At minimum, you will need to provide your SCE username and password. This program assumes you have already saved a payment card to your account, to avoid handling CC numbers.

### Native executable

### Docker

## Troubleshooting
Enable debug logging for secret retrieval by uncommenting the Logging:LogLevel:HcpVaultSecretsConfigProvider.HcpVaultSecretsConfigurationProvider section.
