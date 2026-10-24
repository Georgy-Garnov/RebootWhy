using RebootWhy.Core;
using RebootWhy.Core.Text;

namespace RebootWhy.App.ViewModels;

public sealed class RebootItemViewModel(RebootRecord record)
{
    public RebootRecord Record => record;

    public string TimeText => Display.LocalDateTime(record.BootTimeUtc);

    public string Title => record.Title;

    public string BootTypeText => Display.BootType(record.BootType);

    public string ConfidenceText => Display.Confidence(record.Confidence);

    /// <summary>Marker color in the list: Critical, Warning, Normal or Info.</summary>
    public string Severity => record.Cause switch
    {
        RebootCause.BlueScreen or RebootCause.PowerLossOrHang => "Critical",
        RebootCause.PowerButtonHold or RebootCause.SleepTransitionFailure => "Warning",
        RebootCause.Unknown or RebootCause.Hibernation => "Info",
        _ => "Normal",
    };

    public IReadOnlyList<Fact> Facts { get; } = RecordFacts.Build(record);

    public IReadOnlyList<string> Updates => record.Updates;

    public bool HasUpdates => record.Updates.Count > 0;

    public IReadOnlyList<string> Notes => record.Notes;

    public bool HasNotes => record.Notes.Count > 0;

    public IReadOnlyList<string> Evidence { get; } = record.Evidence.Select(EventDescriber.Line).ToList();

    public string ToClipboardText() => RecordText.Build(record);
}
