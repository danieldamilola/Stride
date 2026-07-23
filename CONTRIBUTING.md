# Contributing to Stride

First off, thank you for considering contributing to Stride! It's people like you that make open-source software such a great community to learn, inspire, and create.

This document serves as a set of guidelines for contributing to Stride. These are guidelines, not rules. Use your best judgment, and feel free to propose changes to this document in a pull request.

---

## Code of Conduct

By participating in this project, you are expected to uphold a welcoming, inclusive, and professional environment. Please ensure that all communication in issues, pull requests, and discussions remains respectful.

## How Can I Contribute?

### Reporting Bugs

If you find a bug in the browser, please check the [Issue Tracker](https://github.com/danieldamilola/Stride/issues) first to see if it has already been reported. If not, feel free to open a new issue.

When reporting a bug, please include:
- Your operating system and version.
- The version of Stride you are running.
- Detailed, step-by-step instructions to reproduce the issue.
- Any relevant logs (found at `%LocalAppData%\StrideBrowser\crash.log` or `stride.log`).

### Suggesting Enhancements

We are always looking for ways to make Stride better! If you have an idea for a new feature or an improvement to the UI/UX:
1. Check the issue tracker for similar suggestions.
2. Open a "Feature Request" issue.
3. Describe the problem your feature solves and propose a solution. Include mockups or reference images if possible.

### Contributing Code

If you want to get your hands dirty and write some code, follow the process below.

#### 1. Development Setup

To build and run Stride locally, ensure you have the following installed:
- Windows 10 (1809+) or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

Clone the repository and build:
```bash
git clone https://github.com/danieldamilola/Stride.git
cd Stride
dotnet build SpurBrowser.csproj -c Debug
dotnet run --project SpurBrowser.csproj -c Debug
```

#### 2. Branching Strategy

- `main` is our active development branch.
- Always create a new branch from `main` for your work.
- Use descriptive branch names (e.g., `feature/vertical-tabs`, `bugfix/memory-leak`, `chore/update-readme`).

#### 3. Coding Standards

- Follow standard C# and WPF naming conventions.
- Keep the `MainWindow.xaml.cs` (Code-Behind) as clean as possible. Offload logic to `ViewModels` and `Services`.
- Write unit tests for pure logic classes (e.g., Services, Parsers) in the `SpurBrowser.Tests` project.
- **Do not introduce arbitrary NuGet packages** without discussing it in an issue first. We try to keep our dependency tree lean for performance and security reasons.

#### 4. The Pull Request (PR) Process

1. Fork the repository and create your branch from `main`.
2. Make your changes and ensure the project still builds cleanly (`dotnet build`).
3. Run the test suite to ensure nothing is broken (`dotnet test`).
4. Push your branch to your fork and open a Pull Request against our `main` branch.
5. In your PR description, clearly explain:
   - What the PR does.
   - Why it is necessary.
   - How to test it.
6. A maintainer will review your code. We may suggest some changes or improvements before merging.

## The Future of Stride

If you are looking for things to work on, here are a few major milestones we are actively looking for help with:

- **Linux Port (Avalonia):** We have future plans to port Stride to Linux using Avalonia UI to achieve full visual parity with the Windows WPF version. Help architecting the UI decoupling is highly appreciated.
- **Extension API:** Expanding our internal `IExtensionManager` to support standard browser extension manifests.
- **Performance Profiling:** Identifying and fixing memory bottlenecks in the `TabEngine` lifecycle.

Thank you for helping us build a better browser!
