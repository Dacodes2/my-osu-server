# Use Ubuntu as the base image to avoid MCR registry issues
FROM ubuntu:22.04 AS build
WORKDIR /app

# Install prerequisites for .NET SDK
RUN apt-get update && \
    apt-get install -y wget ca-certificates && \
    wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y dotnet-sdk-8.0

# Copy csproj and restore for Linux-x64
COPY *.csproj ./
RUN dotnet restore -r linux-x64

# Copy source and publish as self-contained
COPY . ./
RUN dotnet publish -c Release -o out -r linux-x64 --self-contained true -p:PublishSingleFile=false

# Runtime stage
FROM ubuntu:22.04
WORKDIR /app

# Install only the native dependencies required by .NET (self-contained)
# libicu is needed for globalization, libssl for HTTPS/crypto
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    libicu70 \
    libssl3 \
    ca-certificates \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Copy the self-contained app
COPY --from=build /app/out .

# Grant execute permissions
RUN chmod +x ./OsuPrivateServer

# Configuration
ENV ASPNETCORE_URLS=http://+:5000
# Disable invariant globalization to use ICU (needed for usernames/dates)
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 5000

# Execute the binary directly (no 'dotnet' command needed)
ENTRYPOINT ["./OsuPrivateServer"]
