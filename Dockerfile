#syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PayPowerBill.csproj", "."]
RUN dotnet restore "./PayPowerBill.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "PayPowerBill.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PayPowerBill.csproj" -c Release -o /app/publish

FROM base AS final
USER root
WORKDIR /app
COPY --from=publish /app/publish .
#Need to trigger Playwright install thru code to avoid dependency on sdk in final image - see www.meziantou.net/distributing-applications-that-depend-on-microsoft-playwright.htm
#The other advantage of this is we don't need to track/specify the Linux deps needed - playwright will install those for us.

RUN dotnet /app/PayPowerBill.dll install
#USER app   #Playwright seems to require root at this time
ENTRYPOINT ["dotnet", "PayPowerBill.dll"]