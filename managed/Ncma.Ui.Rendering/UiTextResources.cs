using Ncma.Interop;
using Ncma.Text;

namespace Ncma.Ui.Rendering;

// One lazy native Text context per application, shared by independent UI canvases. Consumers close
// their own runs/fonts first; TextService refuses shutdown while any such resources remain live.
public sealed class UiTextResources : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly PluginLoader _loader = new();
    private TextService? _service;
    private string? _plugins;
    private bool _disposed;
    private void Verify()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI text resources require owner thread."); ObjectDisposedException.ThrowIf(_disposed,this); }
    public TextService Get(string plugins)
    {
        Verify(); string canonical=Path.GetFullPath(plugins);
        if (_plugins is not null && !StringComparer.OrdinalIgnoreCase.Equals(_plugins,canonical)) throw new ArgumentException("Shared UI text resources cannot switch plugin roots.");
        if (_service is null) {
            _loader.Load(canonical,[new("ncma.text",ModuleKind.Text,"NcmaText.dll","NcmaText.dll",1,0,[])]);
            _service=new(_loader.Modules.Single()); _plugins=canonical;
        }
        return _service;
    }
    public IReadOnlyList<PluginModule> Modules { get { Verify(); return _loader.Modules; } }
    public void Dispose()
    {
        if (_disposed) { if (_owner!=Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI text resources require owner thread."); return; }
        Verify(); _service?.Dispose(); _service=null; _loader.Dispose(); _disposed=true;
    }
}
