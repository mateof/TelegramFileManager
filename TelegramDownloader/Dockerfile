#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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
RUN dotnet publish "./TelegramDownloader.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false,Version=0.0.0.0

FROM base AS final
WORKDIR /app

# Instala Python y pip
RUN apt-get update && \
    apt-get install -y python3 python3-pip python3-venv && \
    ln -s /usr/bin/python3 /usr/bin/python && \
    rm -rf /var/lib/apt/lists/*

COPY ./WebDav /app/WebDav
RUN python3 -m venv /app/venv

# Añade el venv al PATH para que "python" y los scripts (uvicorn, etc.) estén disponibles globalmente
ENV PATH="/app/venv/bin:${PATH}"

# Actualiza pip e instala requirements dentro del venv
RUN python -m pip install --upgrade pip
RUN python -m pip install -r /app/WebDav/requirements.txt

# Comprobación opcional para fallar la build si falta uvicorn
RUN python -c "import uvicorn; print('uvicorn OK', uvicorn.__version__)"

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TelegramDownloader.dll"]
