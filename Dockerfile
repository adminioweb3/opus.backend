FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CitationlyBackend.slnx", "./"]
COPY ["Citationly.API/Citationly.API.csproj", "Citationly.API/"]
COPY ["Citationly.Application/Citationly.Application.csproj", "Citationly.Application/"]
COPY ["Citationly.Domain/Citationly.Domain.csproj", "Citationly.Domain/"]
COPY ["Citationly.Infrastructure/Citationly.Infrastructure.csproj", "Citationly.Infrastructure/"]
COPY ["Citationly.Tests/Citationly.Tests.csproj", "Citationly.Tests/"]
COPY ["DbInit/DbInit.csproj", "DbInit/"]

RUN dotnet restore

COPY . .

WORKDIR "/src/Citationly.API"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Citationly.API.dll"]
