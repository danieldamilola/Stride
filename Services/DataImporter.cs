using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StrideBrowser.Models;
using System.Diagnostics;

namespace StrideBrowser.Services;

public static class DataImporter
{
    public static List<ShortcutItem> ImportBookmarksToShortcuts(int maxItems = 10)
    {
        var shortcuts = new List<ShortcutItem>();
        
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pathsToTry = new[]
        {
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Bookmarks"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Bookmarks")
        };

        foreach (var path in pathsToTry)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                using var document = JsonDocument.Parse(json);
                
                // Chrome/Edge bookmarks schema: { "roots": { "bookmark_bar": { "children": [ ... ] } } }
                if (document.RootElement.TryGetProperty("roots", out var roots) &&
                    roots.TryGetProperty("bookmark_bar", out var bookmarkBar) &&
                    bookmarkBar.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        if (shortcuts.Count >= maxItems) break;

                        if (child.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "url")
                        {
                            var name = child.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                            var url = child.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : "";
                            
                            if (!string.IsNullOrEmpty(url))
                            {
                                shortcuts.Add(new ShortcutItem { Name = name ?? "", Url = url });
                            }
                        }
                    }
                }
                
                // If we found some from this browser, stop searching other browsers
                if (shortcuts.Count > 0)
                {
                    Trace.WriteLine($"Successfully imported {shortcuts.Count} bookmarks from {path}");
                    break; 
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error importing bookmarks from {path}: {ex.Message}");
            }
        }

        return shortcuts;
    }
}
