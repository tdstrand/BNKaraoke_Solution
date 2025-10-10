# BNKaraoke Solution

A comprehensive karaoke management system with DJ client, web interface, and API backend.

## Project Structure

- **BNKaraoke.Api** - ASP.NET Core Web API backend
- **BNKaraoke.DJ** - WPF Desktop DJ control application
- **bnkaraoke.web** - React Progressive Web App for users
- **Scripts** - Utility scripts for data management
- **docs** - Project documentation

## Documentation

### Development Setup
- [GitHub Copilot Grok Setup](docs/github-copilot-grok-setup.md) - Configure Grok Code Fast 1 for AI-assisted development
- [OR-Tools Deployment Guide](docs/ortools-deployment.md) - Deploy Google OR-Tools native libraries
- [DJ Client Q&A](docs/dj-client-qa.md) - DJ application documentation
- [Overlay Q&A](docs/overlay-qa.md) - Overlay feature documentation

### ⚡ Quick Start: GitHub Copilot with Grok Code Fast 1

This project is **pre-configured** to use **Grok Code Fast 1** (xAI's fastest model) for AI-assisted development!

**5-Minute Setup:**
1. Install [Visual Studio Code](https://code.visualstudio.com/)
2. Install the [GitHub Copilot extension](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot)
3. Open this project - settings are already configured! ✅
4. Start coding with lightning-fast AI assistance! 🚀

**Learn More:**
- 📖 [Quick Start Guide](docs/QUICK_START.md) - 5-minute setup walkthrough
- 📚 [Detailed Grok Setup Guide](docs/github-copilot-grok-setup.md) - Complete configuration reference

## Technologies

- **.NET 8** - Backend API and DJ client
- **React** - Web frontend
- **SignalR** - Real-time communication
- **Entity Framework Core** - Database access
- **Google OR-Tools** - Optimization algorithms
- **LibVLC** - Video playback
- **Spotify API** - Music metadata

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js (for web client)
- SQL Server or PostgreSQL

### Running the API

```bash
cd BNKaraoke.Api
dotnet restore
dotnet run
```

### Running the DJ Client

```bash
cd BNKaraoke.DJ
dotnet restore
dotnet run
```

### Running the Web Client

```bash
cd bnkaraoke.web
npm install
npm start
```

## Contributing

When contributing to this project, please use GitHub Copilot with Grok Code Fast 1 for consistency. The configuration is already set up in `.vscode/settings.json`.

## License

[Add license information here]
