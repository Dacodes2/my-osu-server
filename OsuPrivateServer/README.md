# OsuPrivateServer

A private osu! server implementation built with ASP.NET Core.

## Features

- **Bancho Protocol Support**: Handles login, score submission, and multiplayer lobby management.
- **Web Interface**: A modern Single Page Application (SPA) for browsing user profiles, rankings, and recent plays.
- **Beatmap Mirror**: Integrated beatmap download and search functionality.
- **Favouriting**: Support for favouriting beatmap sets.

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/) (optional, defaults to SQLite for local development)

### Running Locally

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd OsuPrivateServer
   ```

2. Run the server:
   ```bash
   dotnet run
   ```

3. Access the web interface at `http://localhost:5000` (or the port specified in console output).

## Configuration

The server uses `appsettings.json` and environment variables for configuration.

- `DATABASE_URL`: Connection string for PostgreSQL (or use default SQLite).
- `WEBSITE_URL`: Public URL for the server (used for avatars and redirects).

## Deployment

This project is ready for deployment on platforms like Render.com.

1. Create a new Web Service on Render.
2. Connect your GitHub repository.
3. Set the Build Command to `dotnet build -c Release`.
4. Set the Start Command to `dotnet run -c Release`.
5. Add environment variables as needed.

## License

[MIT](LICENSE)
