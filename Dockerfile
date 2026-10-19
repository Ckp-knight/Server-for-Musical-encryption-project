# syntax=docker/dockerfile:1

# --- Build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY MusicalEncryptionServer.csproj ./
RUN dotnet restore MusicalEncryptionServer.csproj

COPY . .
RUN dotnet publish MusicalEncryptionServer.csproj -c Release -o /app/publish --no-restore

# --- Runtime stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
# Cloud Run sets PORT at runtime; Program.cs reads it and binds 0.0.0.0:$PORT.
ENV PORT=8080
EXPOSE 8080

# Run as the non-root user already built into the official ASP.NET image.
USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MusicalEncryptionServer.dll"]
