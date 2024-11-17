#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG tag

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TelegramDownloader.csproj", "."]
RUN dotnet restore "TelegramDownloader.csproj"
COPY . .
RUN echo $(ls -1 ./)
WORKDIR "/src/."
RUN dotnet build "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false,Version=$tag

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramDownloader.dll"]
