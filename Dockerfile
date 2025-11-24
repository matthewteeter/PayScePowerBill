#syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
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
## Install dependencies before running Playwright install
RUN apt-get update && apt-get -y install libxcb-shm0 libx11-xcb1 libx11-6 libxcb1 libxext6 libxrandr2 libxcomposite1 libxcursor1 libxdamage1 libxfixes3 libxi6 libgtk-3-0t64 libpangocairo-1.0-0 libpango-1.0-0 libatk1.0-0t64 libcairo-gobject2 libcairo2 libgdk-pixbuf-2.0-0 libglib2.0-0t64 libxrender1 libasound2t64 libfreetype6 libfontconfig1 libdbus-1-3 && rm -rf /var/lib/apt/lists/*
##Need to trigger Playwright install thru code to avoid dependency on sdk in final image - see www.meziantou.net/distributing-applications-that-depend-on-microsoft-playwright.htm
RUN dotnet /app/PayPowerBill.dll install
##USER app   #Playwright seems to require root at this time
ENTRYPOINT ["dotnet", "PayPowerBill.dll"]