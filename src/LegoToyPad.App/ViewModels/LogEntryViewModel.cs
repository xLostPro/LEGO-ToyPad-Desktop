using LegoToyPad.Core;

namespace LegoToyPad.App.ViewModels;

public sealed class LogEntryViewModel(LogEntry entry)
{
    public string Time { get; } = entry.Timestamp.ToString("HH:mm:ss");
    public string Message { get; } = entry.Message;
    public LogLevel Level { get; } = entry.Level;
    public bool IsSuccess => Level == LogLevel.Success;
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level == LogLevel.Error;
    public bool IsInfo => Level == LogLevel.Info;

    public string LevelText => Level switch
    {
        LogLevel.Success => "Success",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        _ => "Info"
    };

    public override string ToString() => $"[{Time}] {LevelText.ToUpperInvariant(),-7} {Message}";
}
