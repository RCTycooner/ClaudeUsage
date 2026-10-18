using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClaudeUsage.Storage;

namespace ClaudeUsage.Usage;

/// <summary>
/// Latest snapshot, last error and the history, for binding from the window. Updated on the UI thread only.
/// <see cref="History"/> is replaced (not mutated) on every change so bindings see a new value.
/// </summary>
public sealed class UsageState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after any change, once per update (convenient for redrawing the chart).</summary>
    public event EventHandler? Changed;

    /// <summary>Last successfully parsed snapshot (may come from history on startup, then RawText is empty).</summary>
    public UsageSnapshot? Latest { get; private set; }

    /// <summary>When <see cref="Latest"/> was taken.</summary>
    public DateTimeOffset? LastUpdated => Latest?.Taken;

    /// <summary>Set when the most recent poll failed; cleared by the next success.</summary>
    public UsagePollFailure? LastError { get; private set; }

    public IReadOnlyList<UsageSample> History { get; private set; } = [];

    public void SetSnapshot(UsageSnapshot snapshot, IReadOnlyList<UsageSample> history)
    {
        Latest = snapshot;
        LastError = null;
        History = history;
        Raise(nameof(Latest));
        Raise(nameof(LastUpdated));
        Raise(nameof(LastError));
        Raise(nameof(History));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFailure(UsagePollFailure failure)
    {
        LastError = failure;
        Raise(nameof(LastError));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
