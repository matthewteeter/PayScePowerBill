#syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
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
#RUN apt-get update && apt-get -y install libasound2 libx11-xcb1 libxcursor1 libgtk-3-0 libpangocairo-1.0-0 libcairo-gobject2 libgdk-pixbuf-2.0-0 libdbus-glib-1-2 \
	#&& rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
##Need to trigger Playwright install thru code to avoid dependency on sdk in final image - see www.meziantou.net/distributing-applications-that-depend-on-microsoft-playwright.htm
RUN dotnet /app/PayPowerBill.dll install
## Using this dependency list vs relying on Playwright's install-deps saves over 500MB in image size (uncompressed)
RUN <<-DEPS
	apt-get update &&
	apt-get -y install libasound2 libx11-xcb1 libxcursor1 libgtk-3-0 libpangocairo-1.0-0 libcairo-gobject2 libgdk-pixbuf-2.0-0 libdbus-glib-1-2 &&
	rm -rf /var/lib/apt/lists/* &&
	echo "workaround"
DEPS
##USER app   #Playwright seems to require root at this time
ENTRYPOINT ["dotnet", "PayPowerBill.dll"]