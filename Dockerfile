# PayGuard AI - Production Docker Image
# Multi-stage build for optimized image size

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["PayGuardAI.slnx", "./"]
COPY ["src/PayGuardAI.Core/PayGuardAI.Core.csproj", "src/PayGuardAI.Core/"]
COPY ["src/PayGuardAI.Data/PayGuardAI.Data.csproj", "src/PayGuardAI.Data/"]
COPY ["src/PayGuardAI.Web/PayGuardAI.Web.csproj", "src/PayGuardAI.Web/"]

# Restore dependencies
RUN dotnet restore "src/PayGuardAI.Web/PayGuardAI.Web.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/src/PayGuardAI.Web"
RUN dotnet build "PayGuardAI.Web.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "PayGuardAI.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install SQLite for database support
RUN apt-get update && \
    apt-get install -y sqlite3 && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create directory for SQLite database
RUN mkdir -p /app/data && chmod 777 /app/data

# Set environment variables
ENV ASPNETCORE_URLS=http://+:$PORT
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port (Heroku will set PORT environment variable)
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:$PORT/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "PayGuardAI.Web.dll"]
