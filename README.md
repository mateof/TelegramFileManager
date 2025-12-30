# TelegramFileManager
With telegramFileManager you can have your own unlimited cloud and manage it through a file system in a simple way.
You will be able to store files of unlimited size.

<img width="2499" height="890" alt="TFM Home" src="https://github.com/user-attachments/assets/98f28256-f1cf-4a67-9bfe-e23951525bbb" />

## Getting started
[Wiki](https://github.com/mateof/TelegramFileManager/wiki)

<img width="2190" height="890" alt="TFM File Manager" src="https://github.com/user-attachments/assets/742c6e61-b49e-4c5e-be63-dc9cb65f8689" />

<img width="1318" height="861" alt="TFM Audio Player FM" src="https://github.com/user-attachments/assets/d6d818cd-f8c8-4059-9d90-35f1b12096b3" />

<img width="1480" height="852" alt="TFM Video player FM" src="https://github.com/user-attachments/assets/1d2c97ce-2b6a-4238-97bd-efc863918d3a" />


## Installation
### Previous steps
1. Obtain App_Hash and App_Id in [API development tools](https://my.telegram.org/apps). [How to](https://core.telegram.org/api/obtaining_api_id)
2. Rename file `Configuration/config.example.json` as `Configuration/config.json` and modify it:
  - "mongo_connection_string": "mongodb://\<username>:\<password>@\<server>:\<port>".
  - Complete Api_hash and Api_id.

### PC
- Install [MongoDB](https://www.mongodb.com/try/download/community)
- Install [.Net SDK](https://dotnet.microsoft.com/en-us/download)
- Install [.Net Runtime Run server apps](https://dotnet.microsoft.com/en-us/download/dotnet/6.0/runtime?cid=getdotnetcore&os=windows&arch=x64)

### Docker
Docker Compose.

```
version: "3"
services:
  telegramdownloader:
    image: ghcr.io/mateof/telegramfilemanager:latest
    deploy:
      resources:
        limits:
          memory: 6G
    container_name: telegramfilemanager
    environment:
      connectionString: "mongodb://<username/>:<password/>@mongodb_container:27017"
      api_id: ""
      hash_id: ""
      DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE: false
      DOTNET_CLI_TELEMETRY_OPTOUT: true
    restart: unless-stopped
    depends_on:
      - mongodb_container
    ports:
      - "8015:8080"
    volumes:
      - <local folder/>:/app/local
      - <folder logs/>:/app/logs
      - <user data/>:/app/datauser
  mongodb_container:
    image: mongo:latest
    container_name: mongodb
    restart: unless-stopped
    environment:
      MONGO_INITDB_ROOT_USERNAME: 
      MONGO_INITDB_ROOT_PASSWORD: 
    ports:
      - 27017:27017
    volumes:
      - <path for mongo data/>:/data/db
  # For debug    
  mongo-express:
    image: mongo-express
    restart: unless-stopped
    environment:
        - ME_CONFIG_MONGODB_SERVER=mongodb
        - ME_CONFIG_MONGODB_PORT=27017
        - ME_CONFIG_MONGODB_ENABLE_ADMIN=true
        - ME_CONFIG_MONGODB_AUTH_DATABASE=admin
        - ME_CONFIG_MONGODB_AUTH_USERNAME=
        - ME_CONFIG_MONGODB_AUTH_PASSWORD=
        - ME_CONFIG_BASICAUTH_USERNAME=
        - ME_CONFIG_BASICAUTH_PASSWORD=
    depends_on:
      - mongodb_container
    ports:
      - "27000:8081"
```
## Compile

> dotnet publish -c Release --output ./MyTargetFolder .\TelegramDownloader.csproj

## Usage

- Create a private or public channel on Telegram.
- The new channel will appear on the left side panel.
- Click on the folder icon and you will access the file manager.
- Go to the tab called `local`.
- Select the files or folders you want to upload to Telegram.
- Click on the `Upload Telegram` option in the menu.
- You can see the upload progress by clicking on the three dots located in the upper right position, which will display the right side panel. Then click on the `Tasks` option.
- When the upload tasks are finished, you will be able to see the uploaded files in the file manager, in the `Remote` tab.
- When you want, you can download the files again to a location on your local computer, selecting the files or folders and clicking the `Download to Local` button.

## Mentions

- [Syncfusion](https://www.syncfusion.com/blazor-components)
- [Blazor Bootstrap](https://demos.blazorbootstrap.com/)
- [WTelegramClient](https://github.com/wiz0u/WTelegramClient)

<!---

## Name

## Description

## Badges

## Visuals


## Support


## Roadmap


## Contributing


## Authors and acknowledgment

-->

## License
- GPLv3

<!--

## Project status

-->
