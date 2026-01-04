<#
.SYNOPSIS
    Script para cambiar entre WTelegramClient oficial (NuGet) y custom (submodule)

.DESCRIPTION
    Permite alternar entre:
    - Librería oficial de NuGet (WTelegramClient)
    - Librería custom con optimizaciones de transferencia (submodule)

    Modifica automáticamente:
    - TelegramDownloader.csproj
    - Dockerfile
    - GitHub Actions workflows

.EXAMPLE
    .\switch-wtelegramclient.ps1
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("nuget", "custom", "status")]
    [string]$Mode
)

$ErrorActionPreference = "Stop"
$ProjectFile = "TelegramDownloader\TelegramDownloader.csproj"
$DockerFile = "TelegramDownloader\Dockerfile"
$SubmodulePath = "libs\WTelegramClient"
$NuGetVersion = "4.3.14"

$WorkflowFiles = @(
    ".github\workflows\docker-image.yml",
    ".github\workflows\release-docker-image.yml",
    ".github\workflows\buildrelease.yml"
)

function Get-CurrentMode {
    $content = Get-Content $ProjectFile -Raw
    if ($content -match '<ProjectReference Include=".*WTelegramClient') {
        return "custom"
    } elseif ($content -match '<PackageReference Include="WTelegramClient"') {
        return "nuget"
    }
    return "unknown"
}

function Show-Status {
    $currentMode = Get-CurrentMode
    Write-Host ""
    Write-Host "=== Estado actual de WTelegramClient ===" -ForegroundColor Cyan
    Write-Host ""

    if ($currentMode -eq "custom") {
        Write-Host "  Modo: " -NoNewline
        Write-Host "CUSTOM (submodule)" -ForegroundColor Green

        if (Test-Path $SubmodulePath) {
            Push-Location $SubmodulePath
            $commitId = git rev-parse HEAD 2>$null
            $commitMsg = git log -1 --pretty=format:"%s" 2>$null
            $branch = git branch --show-current 2>$null
            Pop-Location

            Write-Host "  Ruta: $SubmodulePath"
            Write-Host "  Rama: $branch"
            Write-Host "  Commit: $commitId"
            Write-Host "  Mensaje: $commitMsg"
        }
    } elseif ($currentMode -eq "nuget") {
        Write-Host "  Modo: " -NoNewline
        Write-Host "NUGET (oficial)" -ForegroundColor Yellow
        Write-Host "  Version: $NuGetVersion"
    } else {
        Write-Host "  Modo: " -NoNewline
        Write-Host "DESCONOCIDO" -ForegroundColor Red
    }
    Write-Host ""
}

function Update-WorkflowsForNuGet {
    Write-Host "  Actualizando GitHub Actions workflows..." -ForegroundColor White

    foreach ($workflowFile in $WorkflowFiles) {
        if (Test-Path $workflowFile) {
            $content = Get-Content $workflowFile -Raw

            # Quitar submodules: recursive del checkout
            $content = $content -replace '(- uses: actions/checkout@v4)\s*\n\s*with:\s*\n\s*submodules: recursive', '$1'

            # Cambiar contexto de docker build de . a ./TelegramDownloader
            $content = $content -replace 'docker build \. -f \./TelegramDownloader/Dockerfile', 'docker build ./TelegramDownloader -f ./TelegramDownloader/Dockerfile'

            Set-Content $workflowFile -Value $content -NoNewline
            Write-Host "    [OK] $workflowFile" -ForegroundColor Green
        }
    }
}

function Update-WorkflowsForCustom {
    Write-Host "  Actualizando GitHub Actions workflows..." -ForegroundColor White

    foreach ($workflowFile in $WorkflowFiles) {
        if (Test-Path $workflowFile) {
            $content = Get-Content $workflowFile -Raw

            # Añadir submodules: recursive al checkout (si no existe ya)
            if ($content -notmatch 'submodules: recursive') {
                $content = $content -replace '(- uses: actions/checkout@v4)(\s*\n)(\s*-|\s*\n\s*-)', "`$1`n      with:`n        submodules: recursive`$2`$3"

                # Patrón alternativo para buildrelease.yml que tiene nombre
                $content = $content -replace "(- name: '📄 Checkout'\s*\n\s*uses: actions/checkout@v4)(\s*\n)(\s*-)", "`$1`n        with:`n          submodules: recursive`$2`$3"
            }

            # Cambiar contexto de docker build de ./TelegramDownloader a .
            $content = $content -replace 'docker build \./TelegramDownloader -f \./TelegramDownloader/Dockerfile', 'docker build . -f ./TelegramDownloader/Dockerfile'

            Set-Content $workflowFile -Value $content -NoNewline
            Write-Host "    [OK] $workflowFile" -ForegroundColor Green
        }
    }
}

function Update-DockerfileForNuGet {
    $dockerContent = @'
#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Alpine base image (~110MB vs ~220MB Debian)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build argument to optionally include FFmpeg (default: true)
# Set to "false" to create a lighter image without video transcoding support
ARG INCLUDE_FFMPEG=true

# Install ICU libraries for globalization support (required by BlazorBootstrap NumberInput)
# Optionally install FFmpeg for video transcoding support (MKV, AVI, WMV, etc.)
RUN apk add --no-cache icu-libs && \
    if [ "$INCLUDE_FFMPEG" = "true" ]; then \
        apk add --no-cache ffmpeg; \
    fi
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0.0
WORKDIR /src
COPY ["TelegramDownloader.csproj", "."]
RUN dotnet restore "TelegramDownloader.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0.0
RUN dotnet publish "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false,InformationalVersion=$VERSION

FROM base AS final
WORKDIR /app

# Install Python (Alpine uses apk instead of apt)
RUN apk add --no-cache python3 py3-pip && \
    ln -sf /usr/bin/python3 /usr/bin/python

COPY ./WebDav /app/WebDav

# Create venv and install dependencies in single layer
RUN python3 -m venv /app/venv && \
    /app/venv/bin/pip install --no-cache-dir --upgrade pip && \
    /app/venv/bin/pip install --no-cache-dir -r /app/WebDav/requirements.txt && \
    /app/venv/bin/python -c "import uvicorn; print('uvicorn OK', uvicorn.__version__)"

ENV PATH="/app/venv/bin:${PATH}"

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramDownloader.dll"]
'@
    Set-Content $DockerFile -Value $dockerContent -NoNewline
    Write-Host "    [OK] $DockerFile" -ForegroundColor Green
}

function Update-DockerfileForCustom {
    $dockerContent = @'
#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Alpine base image (~110MB vs ~220MB Debian)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build argument to optionally include FFmpeg (default: true)
# Set to "false" to create a lighter image without video transcoding support
ARG INCLUDE_FFMPEG=true

# Install ICU libraries for globalization support (required by BlazorBootstrap NumberInput)
# Optionally install FFmpeg for video transcoding support (MKV, AVI, WMV, etc.)
RUN apk add --no-cache icu-libs && \
    if [ "$INCLUDE_FFMPEG" = "true" ]; then \
        apk add --no-cache ffmpeg; \
    fi
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0.0
WORKDIR /src

# Copy submodule (WTelegramClient) first
COPY ["libs/WTelegramClient/src/WTelegramClient.csproj", "libs/WTelegramClient/src/"]
COPY ["libs/WTelegramClient/generator/MTProtoGenerator.csproj", "libs/WTelegramClient/generator/"]

# Copy main project
COPY ["TelegramDownloader/TelegramDownloader.csproj", "TelegramDownloader/"]

# Restore all projects
RUN dotnet restore "TelegramDownloader/TelegramDownloader.csproj"

# Copy all source files
COPY libs/WTelegramClient/ libs/WTelegramClient/
COPY TelegramDownloader/ TelegramDownloader/

WORKDIR "/src/TelegramDownloader"
RUN dotnet build "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0.0
WORKDIR "/src/TelegramDownloader"
RUN dotnet publish "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false,InformationalVersion=$VERSION

FROM base AS final
WORKDIR /app

# Install Python (Alpine uses apk instead of apt)
RUN apk add --no-cache python3 py3-pip && \
    ln -sf /usr/bin/python3 /usr/bin/python

COPY TelegramDownloader/WebDav /app/WebDav

# Create venv and install dependencies in single layer
RUN python3 -m venv /app/venv && \
    /app/venv/bin/pip install --no-cache-dir --upgrade pip && \
    /app/venv/bin/pip install --no-cache-dir -r /app/WebDav/requirements.txt && \
    /app/venv/bin/python -c "import uvicorn; print('uvicorn OK', uvicorn.__version__)"

ENV PATH="/app/venv/bin:${PATH}"

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramDownloader.dll"]
'@
    Set-Content $DockerFile -Value $dockerContent -NoNewline
    Write-Host "    [OK] $DockerFile" -ForegroundColor Green
}

function Switch-ToNuGet {
    Write-Host ""
    Write-Host "=== Cambiando a WTelegramClient oficial (NuGet) ===" -ForegroundColor Yellow
    Write-Host ""

    # Modificar csproj
    $content = Get-Content $ProjectFile -Raw

    # Reemplazar ProjectReference por PackageReference
    $pattern = '(?s)<!-- WTelegramClient personalizado.*?-->.*?<ProjectReference Include="[^"]*WTelegramClient[^"]*\.csproj"\s*/>\s*<!-- Para volver a NuGet.*?-->\s*<!-- <PackageReference Include="WTelegramClient"[^>]*/> -->'
    $replacement = "<PackageReference Include=`"WTelegramClient`" Version=`"$NuGetVersion`" />"

    if ($content -match $pattern) {
        $content = $content -replace $pattern, $replacement
    } else {
        # Patrón alternativo más simple
        $content = $content -replace '<ProjectReference Include="[^"]*WTelegramClient[^"]*\.csproj"\s*/>', $replacement
    }

    Set-Content $ProjectFile -Value $content -NoNewline
    Write-Host "  [OK] Actualizado $ProjectFile" -ForegroundColor Green

    # Modificar Dockerfile
    Write-Host "  Actualizando Dockerfile..." -ForegroundColor White
    Update-DockerfileForNuGet

    # Modificar workflows
    Update-WorkflowsForNuGet

    Write-Host ""
    Write-Host "  [OK] Cambiado a NuGet v$NuGetVersion" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Archivos modificados:" -ForegroundColor Cyan
    Write-Host "    - $ProjectFile"
    Write-Host "    - $DockerFile"
    foreach ($wf in $WorkflowFiles) {
        Write-Host "    - $wf"
    }
    Write-Host ""
    Write-Host "  Ejecuta 'git status' para ver los cambios" -ForegroundColor White
    Write-Host ""
}

function Switch-ToCustom {
    Write-Host ""
    Write-Host "=== Cambiando a WTelegramClient custom (submodule) ===" -ForegroundColor Green
    Write-Host ""

    # Verificar si el submodule existe
    if (-not (Test-Path $SubmodulePath)) {
        Write-Host "  El submodule no existe. Inicializando..." -ForegroundColor Yellow
        git submodule add https://github.com/mateof/MyCustomWTelegramClient.git $SubmodulePath
        git submodule update --init --recursive
    }

    # Pedir commit ID
    Write-Host ""
    Write-Host "  Commits recientes en el submodule:" -ForegroundColor Cyan
    Push-Location $SubmodulePath
    git fetch origin 2>$null
    git log origin/master --oneline -5 2>$null
    Pop-Location
    Write-Host ""

    $commitId = Read-Host "  Introduce el Commit ID (Enter para ultimo de 'master')"

    if ([string]::IsNullOrWhiteSpace($commitId)) {
        $commitId = "origin/master"
    }

    # Actualizar submodule al commit especificado
    Push-Location $SubmodulePath
    git checkout $commitId 2>$null
    $actualCommit = git rev-parse HEAD
    $commitMsg = git log -1 --pretty=format:"%s"
    Pop-Location

    Write-Host ""
    Write-Host "  [OK] Submodule en commit: $actualCommit" -ForegroundColor Green
    Write-Host "       Mensaje: $commitMsg"

    # Modificar csproj
    $content = Get-Content $ProjectFile -Raw

    # Reemplazar PackageReference por ProjectReference
    $newRef = @"
<!-- WTelegramClient personalizado con optimizaciones de transferencia (submodule) -->
    <ProjectReference Include="..\libs\WTelegramClient\src\WTelegramClient.csproj" />
    <!-- Para volver a NuGet oficial, ejecuta: .\switch-wtelegramclient.ps1 nuget -->
    <!-- <PackageReference Include="WTelegramClient" Version="$NuGetVersion" /> -->
"@

    $content = $content -replace '<PackageReference Include="WTelegramClient"[^/]*/>', $newRef

    Set-Content $ProjectFile -Value $content -NoNewline
    Write-Host "  [OK] Actualizado $ProjectFile" -ForegroundColor Green

    # Modificar Dockerfile
    Write-Host "  Actualizando Dockerfile..." -ForegroundColor White
    Update-DockerfileForCustom

    # Modificar workflows
    Update-WorkflowsForCustom

    # Registrar cambio en submodule
    git add $SubmodulePath 2>$null

    Write-Host ""
    Write-Host "  [OK] Cambiado a custom library" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Archivos modificados:" -ForegroundColor Cyan
    Write-Host "    - $ProjectFile"
    Write-Host "    - $DockerFile"
    foreach ($wf in $WorkflowFiles) {
        Write-Host "    - $wf"
    }
    Write-Host "    - $SubmodulePath (submodule)"
    Write-Host ""
    Write-Host "  Ejecuta 'git status' para ver los cambios" -ForegroundColor White
    Write-Host ""
}

# Main
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WTelegramClient Switcher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not $Mode) {
    Show-Status
    Write-Host "Opciones disponibles:" -ForegroundColor White
    Write-Host "  1) Cambiar a NuGet (oficial)"
    Write-Host "  2) Cambiar a Custom (submodule)"
    Write-Host "  3) Ver estado actual"
    Write-Host "  4) Salir"
    Write-Host ""
    $choice = Read-Host "Selecciona una opcion (1-4)"

    switch ($choice) {
        "1" { $Mode = "nuget" }
        "2" { $Mode = "custom" }
        "3" { $Mode = "status" }
        "4" { exit 0 }
        default { Write-Host "Opcion no valida" -ForegroundColor Red; exit 1 }
    }
}

switch ($Mode) {
    "nuget" { Switch-ToNuGet }
    "custom" { Switch-ToCustom }
    "status" { Show-Status }
}
