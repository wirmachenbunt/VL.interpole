using System.Reactive.Subjects;
using VL.Core.CompilerServices;
using VL.Lib.Collections;

namespace VL.Interpole;

// A dynamic enum whose entries are the current timeline's marker names — so the Interpole node can
// expose a "Marker" dropdown pin in the patch and a "Jump" trigger that seeks to the selected marker
// and plays from there. The node syncs the entries from its document each frame (see InterpoleNode).
//
// Note: the definition is an app-global singleton (the vvvv dynamic-enum model), so with several
// Interpole nodes the dropdown reflects whichever node synced last. Fine for the common single-
// timeline case; a per-instance enum would need a different mechanism.
[Serializable]
public class InterpoleMarker : DynamicEnumBase<InterpoleMarker, InterpoleMarkerDefinition>
{
    public InterpoleMarker(string value) : base(value) { }

    [CreateDefault]
    public static InterpoleMarker CreateDefault() => CreateDefaultBase();
}

public class InterpoleMarkerDefinition : DynamicEnumDefinitionBase<InterpoleMarkerDefinition>
{
    // name -> frame. The tag carries the frame so a consumer could read it directly; the node resolves
    // the jump target from the document, so duplicate names simply pick the first there.
    private readonly Dictionary<string, object> _entries = new();
    private readonly Subject<object> _trigger = new();   // pulsed to tell vvvv the entry set changed

    [CreateDefault]
    public static InterpoleMarkerDefinition CreateDefault() => Instance;

    // Replace the entry set with the given markers. Only pulses the change trigger when the set of
    // names actually differs, so calling this every node Update is cheap and doesn't churn the UI.
    public static void Sync(IEnumerable<(string name, int frame)> markers) => Instance.SetEntries(markers);

    private void SetEntries(IEnumerable<(string name, int frame)> markers)
    {
        var next = new Dictionary<string, object>();
        foreach (var (name, frame) in markers)
            if (!string.IsNullOrWhiteSpace(name))
                next[name] = frame;

        bool sameNames = next.Count == _entries.Count && next.Keys.All(_entries.ContainsKey);
        _entries.Clear();
        foreach (var kv in next) _entries[kv.Key] = kv.Value;
        if (!sameNames) _trigger.OnNext("");
    }

    protected override IReadOnlyDictionary<string, object> GetEntries() => _entries;

    protected override IObservable<object> GetEntriesChangedObservable() => _trigger;

    protected override bool AutoSortAlphabetically => false;   // keep document/marker order
}
