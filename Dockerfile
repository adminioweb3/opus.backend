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

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
        ca-certificates \
        curl && \
    rm -rf /var/lib/apt/lists/*

# Playwright's NuGet package only ships the driver, not an actual browser binary — without this,
# every scrape (Page Auditor's analyze, Content's competitor analysis) 500s at runtime with
# "Executable doesn't exist at ~/.cache/ms-playwright/...". playwright.ps1 (generated into the
# publish output alongside Citationly.API.dll) needs pwsh to run; --with-deps also installs every
# OS-level library headless Chromium needs, so there's no separate apt list to hand-maintain.
#
# Installed from Microsoft's version-pinned release tarball rather than their apt repo: the repo
# is keyed by Debian codename (bookworm/trixie/...), and guessing which one matches whatever
# Debian version this particular .NET base image ships with is exactly the kind of thing that
# silently breaks the next time the base image moves — a static binary has no such dependency.
RUN curl -fsSL https://github.com/PowerShell/PowerShell/releases/download/v7.4.6/powershell-7.4.6-linux-x64.tar.gz -o /tmp/powershell.tar.gz && \
    mkdir -p /opt/microsoft/powershell/7 && \
    tar zxf /tmp/powershell.tar.gz -C /opt/microsoft/powershell/7 && \
    chmod +x /opt/microsoft/powershell/7/pwsh && \
    ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh && \
    rm /tmp/powershell.tar.gz

WORKDIR /app

COPY --from=build /app/publish .

RUN pwsh playwright.ps1 install --with-deps chromium

ENTRYPOINT ["dotnet", "Citationly.API.dll"]
