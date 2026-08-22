# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files accounting for the src subdirectory
COPY ["*.sln", "./"]
COPY ["src/*/*.csproj", "src/"]
RUN dotnet restore "src/spool-dat-torrent.web/spool-dat-torrent.web.csproj"

# Copy the rest of the source code and build
COPY . .
WORKDIR "/src/src/spool-dat-torrent.web"
RUN dotnet publish "spool-dat-torrent.web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final Lightweight Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

RUN mkdir -p /app/data /app/dats

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "spool-dat-torrent.web.dll"]