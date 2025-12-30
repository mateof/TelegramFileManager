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
