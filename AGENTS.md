# AGENTS.md
## Writing

Writing follows the unslop skill in `~/.config/opencode/skills/unslop/`. Read it before writing any reply, commit message, PR description, or doc change. No em dashes, no parentheses, no filler, no AI vocabulary. Say what the code does, not how it feels.

## The project

Stride is a Windows desktop web browser. It is a C# WPF app on .NET 9 that embeds the Microsoft Edge WebView2 engine. The UI is XAML, the logic lives in ViewModels and Services, and communication with the web content goes through the WebMessageRouter. I'm Daniel. I work on this project. I'm a software developer who likes building cool and random stuff. My GitHub profile is github.com/danieldamilola.

## Build and test

- Build: `dotnet build Stride.csproj -c Debug`
- Run: `dotnet run --project Stride.csproj -c Debug`
- Test: `dotnet test StrideBrowser.Tests/StrideBrowser.Tests.csproj`

Run the tests before finishing any change. A change that breaks the build or the tests is not done.

## Conventions

- Keep `MainWindow.xaml.cs` clean. Put logic in ViewModels and Services, not code-behind.
- Write unit tests for pure logic in the `StrideBrowser.Tests` project. The test project already covers the navigation policy engine, the focus domain matcher, the keyboard shortcut map, the web message router, the update service, and the navigation service. Follow those patterns.
- Do not add NuGet packages without discussing it first. The dependency tree stays lean.
- Use the existing MVVM structure (CommunityToolkit.Mvvm) and DI container. Match the style of the surrounding code.

## Verifying work

Prove a change works against the real artifact, not by reading the code. Run the tests, build the project, and where the change touches the running app, exercise it. A claim that "it compiles" is not proof.

## Release

Releasing follows the release-management skill in `.agents/skills/release-management/`. Read it before touching versioning, the installer, or release notes. 
Note: We have switched to a custom micro-updater. When publishing or building a new installer, you must generate both the `.zip` update package and the native `.exe` installer.
Additionally, you must update `ReleaseNotes.md` immediately after every feature, fix, or change is made, so we always have an up-to-date log of what goes into the next release.