# Agent guidance

## Project

Stride is a Windows desktop browser built with C# WPF on .NET 9 and Microsoft Edge WebView2. XAML defines the UI. ViewModels and Services hold application logic. Web content communicates with the host through WebMessageRouter.

## Writing

Read `.opencode/skills/unslop/SKILL.md` before writing replies, documentation, commit messages or PR descriptions. Use plain, concrete language. Avoid em dashes, en dashes, parentheses and filler.

## Build and test

- Build: `dotnet build Stride.csproj -c Debug`
- Run: `dotnet run --project Stride.csproj -c Debug`
- Test: `dotnet test StrideBrowser.Tests/StrideBrowser.Tests.csproj`

## Implementation

- Follow the existing CommunityToolkit.Mvvm structure and dependency injection registrations.
- Keep `MainWindow.xaml.cs` focused on wiring. Put application logic in ViewModels and Services.
- Match the surrounding code and existing test patterns.
- Cover changed pure logic with meaningful tests in `StrideBrowser.Tests`.
- Discuss new NuGet dependencies with the user before adding them.
- Preserve unrelated staged, unstaged and untracked work. Keep tab drag, drop and window tear-off behavior intact.

## Validation

Run relevant tests before marking a code change complete. Build the project and exercise the actual artifact when application behavior changes. Compilation alone does not prove a fix.

Distinguish source review, automated tests and live behavior checks. Report failures, skipped checks and remaining limits accurately. Do not claim a full suite passed from a focused test run.

## Git

Wait for explicit user confirmation that a fix or feature works before committing or pushing. Do not commit or push autonomously.

## Changelog and packaging

Update `CHANGELOG.md` after every feature, fix or change. Keep pending changes under `Unreleased`. Do not recreate `ReleaseNotes.md`.

Stride uses a custom micro-updater. When building a new installer or publishing an update, generate both the `.zip` update package and the native `.exe` installer.
