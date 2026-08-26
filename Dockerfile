# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything over into the container workspace
COPY . .

# Restore and publish directly from the correct src path
RUN dotnet restore "src/spool-dat-torrent.web/spool-dat-torrent.web.csproj"
RUN dotnet publish "src/spool-dat-torrent.web/spool-dat-torrent.web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final Lightweight Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 6502
ENV ASPNETCORE_HTTP_PORTS=6502

RUN mkdir -p /app/data /app/dats

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "spool-dat-torrent.web.dll"]