# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY EmPeKa.WebAPI.csproj ./
RUN dotnet restore EmPeKa.WebAPI.csproj

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create data directory
RUN mkdir -p /app/data/gtfs
RUN chown -R app:app /app/data

# Copy published app
COPY --from=build /app/publish .

# Use built-in non-root user
USER app

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "EmPeKa.WebAPI.dll"]