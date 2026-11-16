FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/Dfs.Shared/Dfs.Shared.csproj src/Dfs.Shared/
COPY src/Dfs.MetadataServer/Dfs.MetadataServer.csproj src/Dfs.MetadataServer/
COPY src/Dfs.DataServer/Dfs.DataServer.csproj src/Dfs.DataServer/

RUN dotnet restore src/Dfs.MetadataServer/Dfs.MetadataServer.csproj && \
    dotnet restore src/Dfs.DataServer/Dfs.DataServer.csproj

COPY src/Dfs.Shared/ src/Dfs.Shared/
COPY src/Dfs.MetadataServer/ src/Dfs.MetadataServer/
COPY src/Dfs.DataServer/ src/Dfs.DataServer/

RUN dotnet publish src/Dfs.MetadataServer/Dfs.MetadataServer.csproj -c Release --no-restore -o /app/metadata && \
    dotnet publish src/Dfs.DataServer/Dfs.DataServer.csproj -c Release --no-restore -o /app/data

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/metadata ./metadata
COPY --from=build /app/data ./data
COPY deploy/entrypoint.sh ./entrypoint.sh

RUN sed -i 's/\r$//' /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh && \
    mkdir -p /data

ENV DATA_DIR=/data

VOLUME ["/data"]
EXPOSE 7000 7001

ENTRYPOINT ["/app/entrypoint.sh"]
