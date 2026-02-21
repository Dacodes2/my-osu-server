# Use Ubuntu as the base image to avoid MCR registry issues
FROM ubuntu:22.04 AS build
WORKDIR /app

# Install prerequisites
RUN apt-get update && \
    apt-get install -y wget ca-certificates && \
    wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y dotnet-sdk-8.0

# Copy csproj and restore
COPY *.csproj ./
RUN dotnet restore

# Copy source and build
COPY . ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM ubuntu:22.04
WORKDIR /app

# Install Runtime dependencies
RUN apt-get update && \
    apt-get install -y wget ca-certificates aspnetcore-runtime-8.0 && \
    wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y aspnetcore-runtime-8.0 dotnet-runtime-8.0 && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Copy build artifacts
COPY --from=build /app/out .

# Configuration
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# Use full path to dotnet executable to be safe
ENTRYPOINT ["/usr/bin/dotnet", "OsuPrivateServer.dll"]
