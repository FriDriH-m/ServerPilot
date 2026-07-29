FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY [".config/dotnet-tools.json", ".config/"]
COPY ["src/ServerPilot.Domain/ServerPilot.Domain.csproj", "src/ServerPilot.Domain/"]
COPY ["src/ServerPilot.Application/ServerPilot.Application.csproj", "src/ServerPilot.Application/"]
COPY ["src/ServerPilot.Infrastructure/ServerPilot.Infrastructure.csproj", "src/ServerPilot.Infrastructure/"]
COPY ["src/ServerPilot.Api/ServerPilot.Api.csproj", "src/ServerPilot.Api/"]
RUN dotnet restore "src/ServerPilot.Api/ServerPilot.Api.csproj"
RUN dotnet tool restore

COPY . .
RUN dotnet publish "src/ServerPilot.Api/ServerPilot.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM build AS migrate
ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "src/ServerPilot.Infrastructure", "--startup-project", "src/ServerPilot.Infrastructure", "--configuration", "Release", "--no-build"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS final
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServerPilot.Api.dll"]
