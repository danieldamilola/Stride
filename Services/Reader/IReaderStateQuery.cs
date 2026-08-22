using System;
using System.Threading.Tasks;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Narrow query interface that TabEngine uses to check reader state
/// without depending on the full IReaderService.
/// </summary>
public interface IReaderStateQuery
{
    /// <summary>Returns true if the given tab is currently in reader mode.</summary>
    bool IsActive(Guid tabId);

    /// <summary>Exits reader mode for the given tab.</summary>
    Task ExitAsync(Guid tabId);

    /// <summary>Removes the reader session for a closed tab.</summary>
    void RemoveSession(Guid tabId);
}
