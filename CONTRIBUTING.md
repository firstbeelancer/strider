# Contributing to Strider Mail

Thanks for your interest in contributing! This is a hobby project, so please be patient with response times.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/strider.git`
3. Create a branch: `git checkout -b feature/my-feature`
4. Make your changes
5. Push and open a Pull Request

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git
- An IDE with Avalonia support (recommended: JetBrains Rider or Visual Studio 2022 with Avalonia plugin, or VS Code with C# Dev Kit)

## Build & Test

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run --project src/Strider.Host

# Run all tests
dotnet test
```

## Code Style

- Follow standard C# conventions
- Use `CommunityToolkit.Mvvm` for ViewModels (source-generated `[ObservableProperty]`)
- Use nullable reference types (`#nullable enable`)
- XML doc comments on public APIs
- No `#region` blocks
- Async all the way (no `.Result` or `.Wait()`)

## Project Structure

```
src/
├── Strider.Core/           # Domain models, interfaces, services
├── Strider.Infrastructure/ # Implementations (MailKit, SQLite, AI, PGP)
├── Strider.UI/             # Avalonia views, ViewModels, controls
└── Strider.Host/           # Entry point, DI configuration
```

**Rules:**
- `Core` depends on nothing (no UI, no Infrastructure references)
- `Infrastructure` implements `Core` interfaces
- `UI` references `Core` interfaces only (dependency injection handles the rest)

## Commit Messages

Use conventional commits:

```
feat: add PGP key generation dialog
fix: correct thread grouping for forwarded messages
docs: update SPEC.md with calendar requirements
refactor: extract IMAP sync into separate service
test: add unit tests for SignatureService
chore: update Avalonia to 11.2.x
```

## Pull Requests

- One feature/fix per PR
- Include tests for new functionality
- Update documentation if behavior changes
- Keep PRs small and focused

## Security

**Never commit:**
- API keys, tokens, passwords
- `appsettings.json` (use `appsettings.example.json`)
- Database files (`*.db`, `*.db-shm`, `*.db-wal`)
- Private keys (`*.key`, `*.pem`, `*.asc` with private keys)

If you find a security vulnerability, please open a private issue or email the maintainer directly. Do not open a public issue.

## Code of Conduct

Be respectful. This is a hobby project built for fun and learning. Harassment, discrimination, or hostile behavior will result in a ban from the project.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
