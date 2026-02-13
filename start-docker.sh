#!/bin/bash
# Quick start script for local Docker development

echo "🚀 Starting PayGuard AI in Docker..."
echo ""

# Check if Docker is running
if ! docker info > /dev/null 2>&1; then
    echo "❌ Error: Docker is not running. Please start Docker Desktop."
    exit 1
fi

# Build and start containers
echo "📦 Building Docker image..."
docker-compose build

echo ""
echo "▶️  Starting containers..."
docker-compose up -d

echo ""
echo "⏳ Waiting for application to start..."
sleep 10

# Check if application is healthy
if curl -f http://localhost:5054/health > /dev/null 2>&1; then
    echo "✅ PayGuard AI is running!"
    echo ""
    echo "🌐 Access the application at: http://localhost:5054"
    echo "📊 Health check: http://localhost:5054/health"
    echo ""
    echo "📋 View logs: docker-compose logs -f payguard-web"
    echo "🛑 Stop: docker-compose down"
else
    echo "⚠️  Application may still be starting. Check logs:"
    echo "   docker-compose logs -f payguard-web"
fi
