namespace StrideBrowser.Abstractions;

public interface ISingleInstanceService
{
    bool Initialize(string[] args);
    event Action<string[]>? InstanceMessageReceived;
    void Shutdown();
}
