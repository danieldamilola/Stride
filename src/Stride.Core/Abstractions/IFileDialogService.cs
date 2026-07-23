namespace StrideBrowser.Abstractions;

public interface IFileDialogService
{
    string? ShowSaveFileDialog(string title, string defaultFileName, string filter);
    string? ShowOpenFileDialog(string title, string filter);
}
