FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files Citationly.API actually references, so restore doesn't need
# the whole solution (Tests/DbInit aren't published and would otherwise force this layer to
# also carry their csproj files just to satisfy a solution-level restore).
COPY ["Citationly.API/Citationly.API.csproj", "Citationly.API/"]
COPY ["Citationly.Application/Citationly.Application.csproj", "Citationly.Application/"]
COPY ["Citationly.Domain/Citationly.Domain.csproj", "Citationly.Domain/"]
COPY ["Citationly.Infrastructure/Citationly.Infrastructure.csproj", "Citationly.Infrastructure/"]

RUN dotnet restore "Citationly.API/Citationly.API.csproj"

# Copy the rest of the code
COPY . .

# Build the main API project
WORKDIR "/src/Citationly.API"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Citationly.API.dll"]
