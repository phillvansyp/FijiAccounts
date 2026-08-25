# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY FijiAccounts.slnx ./
COPY src/FijiAccounts.Domain/FijiAccounts.Domain.csproj src/FijiAccounts.Domain/
COPY src/FijiAccounts.Web/FijiAccounts.Web.csproj src/FijiAccounts.Web/
COPY tests/FijiAccounts.Domain.Tests/FijiAccounts.Domain.Tests.csproj tests/FijiAccounts.Domain.Tests/
COPY tests/FijiAccounts.Web.Tests/FijiAccounts.Web.Tests.csproj tests/FijiAccounts.Web.Tests/
RUN dotnet restore FijiAccounts.slnx

FROM restore AS build
COPY . .
RUN dotnet build FijiAccounts.slnx --configuration Release --no-restore

FROM build AS test
RUN dotnet test FijiAccounts.slnx --configuration Release --no-build \
    --logger "console;verbosity=minimal"

FROM build AS publish
RUN dotnet publish src/FijiAccounts.Web/FijiAccounts.Web.csproj \
    --configuration Release \
    --no-build \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/data /app/keys && chown -R "$APP_UID:$APP_UID" /app/data /app/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FijiAccounts.Web.dll"]

