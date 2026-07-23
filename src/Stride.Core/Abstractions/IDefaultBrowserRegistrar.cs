namespace StrideBrowser.Abstractions;

public interface IDefaultBrowserRegistrar
{
    void Register();
    void OpenDefaultAppsSettings();
    bool IsRegistered();
}
