using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhraseFlow.Core.Models;

/// <summary>A keyword that expands into a phrase.</summary>
public sealed partial class Snippet : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _keyword = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _content = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private TriggerMode _trigger = TriggerMode.Delimiter;

    [ObservableProperty]
    private CaseMode _caseMode = CaseMode.Adapt;

    [ObservableProperty]
    private bool _expandInsideWords;

    [ObservableProperty]
    private bool _omitDelimiter;

    [ObservableProperty]
    private bool _plainText;

    [ObservableProperty]
    private InsertMethod _insertMethod = InsertMethod.Default;

    [ObservableProperty]
    private int _useCount;

    [ObservableProperty]
    private DateTimeOffset? _lastUsed;

    [ObservableProperty]
    private DateTimeOffset _created = DateTimeOffset.Now;

    /// <summary>Single-line preview of the content for list views.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var text = Content.Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\t', ' ');
            return text.Length > 160 ? text[..160] + "…" : text;
        }
    }

    public Snippet Clone(bool newIdentity = true)
    {
        return new Snippet
        {
            Id = newIdentity ? Guid.NewGuid() : Id,
            Keyword = Keyword,
            Name = Name,
            Content = Content,
            Enabled = Enabled,
            Trigger = Trigger,
            CaseMode = CaseMode,
            ExpandInsideWords = ExpandInsideWords,
            OmitDelimiter = OmitDelimiter,
            PlainText = PlainText,
            InsertMethod = InsertMethod,
            UseCount = newIdentity ? 0 : UseCount,
            LastUsed = newIdentity ? null : LastUsed,
            Created = newIdentity ? DateTimeOffset.Now : Created,
        };
    }

    public override string ToString() => $"{Keyword} → {Preview}";
}
