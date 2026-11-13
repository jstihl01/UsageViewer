using System.Text.Json;
using System.IO;
using UsageViewer.Core;

namespace UsageViewer;

internal sealed record AppSettings(bool FirstRunCompleted=false,bool ShowTimeMarker=false,
    DateTimeOffset? UpdateChecked=null,string? UpdateETag=null,string? AvailableVersion=null);

internal sealed class SettingsStore(string directory,IEventLog log)
{
    private string PathName=>System.IO.Path.Combine(directory,"settings.json");
    internal AppSettings Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length>65536) return new();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName))??new();
        }
        catch (Exception error) { log.Write("settings_read_failed",error:error); return new(); }
    }
    internal void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string temporary=PathName+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(settings));
            File.Move(temporary,PathName,true);
        }
        catch (Exception error) { log.Write("settings_write_failed",error:error); }
    }
}
