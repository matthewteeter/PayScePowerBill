#syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/runtime:8.0-preview-jammy AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-preview-alpine AS build
WORKDIR /src
COPY ["PayPowerBill.csproj", "."]
RUN dotnet restore "./PayPowerBill.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "PayPowerBill.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PayPowerBill.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
#RUN apt-get update && apt-get -y install libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libdrm2 libxkbcommon0 \
	#libgbm1 libasound2 libatspi2.0-0 libcups2 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libpango-1.0-0 libcairo2 \
	#&& rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
#Need to trigger Playwright install thru code to avoid dependency on sdk in final image - see www.meziantou.net/distributing-applications-that-depend-on-microsoft-playwright.htm
#The other advantage of this is we don't need to track/specify the Linux deps needed - playwright will install those for us.
RUN dotnet /app/PayPowerBill.dll install
#USER app   #Playwright seems to require root at this time
ENTRYPOINT ["dotnet", "PayPowerBill.dll"]