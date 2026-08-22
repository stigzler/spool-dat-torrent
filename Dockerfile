FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["spool-dat-torrent.core/spool-dat-torrent.core.csproj", "spool-dat-torrent.core/"]
COPY ["spool-dat-torrent.web/spool-dat-torrent.web.csproj", "spool-dat-torrent.web/"]
RUN dotnet restore "spool-dat-torrent.web/spool-dat-torrent.web.csproj"

COPY . .
WORKDIR "/src/spool-dat-torrent.web"
RUN dotnet publish "spool-dat-torrent.web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

RUN mkdir -p /app/data /app/dats

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "spool-dat-torrent.web.dll"]