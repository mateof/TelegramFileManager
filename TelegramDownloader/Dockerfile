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

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramDownloader.dll"]