using System.Numerics;
using System.Text.Json;
using Ncma.Animation;
namespace Ncma.Editor.Services;

public readonly record struct AnimationGraphPin(Guid Node, string Name, AnimationPinType Type, bool Output, Vector2 Position);
public readonly record struct AnimationGraphNodeBox(Guid Id, Vector2 Position, Vector2 Size);
// Copied authoring geometry only: no native pointers, World, runtime instance or history.
public sealed class AnimationGraphCanvas
{
    private AnimationGraphDefinition? _graph; private readonly HashSet<Guid> _selection = [];
    public Vector2 Pan { get; private set; } = new(24,24);
    public float Zoom { get; private set; } = 1;
    public Guid[] Selection => _selection.Order().ToArray();
    public void Load(AnimationGraphDefinition graph) { if (_graph?.AssetId != graph.AssetId) _selection.Clear(); _graph = AnimationGraphEdits.CopyDraft(graph); _selection.RemoveWhere(id => !graph.Nodes.Any(n => n.Id == id)); }
    public void Select(Guid id, bool toggle = false) { if (_graph?.Nodes.Any(n => n.Id == id) != true) throw new ArgumentException("Unknown node."); if (!toggle) _selection.Clear(); if (toggle && _selection.Contains(id)) _selection.Remove(id); else { if (_selection.Count >= 64) throw new ArgumentException("Selection budget."); _selection.Add(id); } }
    public AnimationGraphNodeBox[] Boxes => _graph?.Nodes.Select(n => new AnimationGraphNodeBox(n.Id, new((float)n.X,(float)n.Y), new(200, 72 + Math.Max(0, Inputs(n).Length - 1)*24))).ToArray() ?? [];
    private static string[] Inputs(AnimationGraphNode n) => n.Kind switch { AnimationNodeKind.Clip => ["speed"], AnimationNodeKind.Blend => ["a","b","weight"], AnimationNodeKind.Output => ["pose"], _ => [] };
    public AnimationGraphPin[] Pins { get {
        if (_graph is null) return []; var pins = new List<AnimationGraphPin>();
        foreach (var n in _graph.Nodes) {
            string[] inputs = Inputs(n); for (int i=0;i<inputs.Length;i++) pins.Add(new(n.Id,inputs[i],inputs[i] is "speed" or "weight" ? AnimationPinType.Float : AnimationPinType.Pose,false,new((float)n.X,(float)n.Y+48+i*24)));
            if (n.Kind != AnimationNodeKind.Output) {
                var kind = n.Kind == AnimationNodeKind.Parameter ? _graph.Parameters.SingleOrDefault(p=>p.Id==n.ParameterId)?.Kind : null;
                var type = kind switch { AnimationParameterKind.Float=>AnimationPinType.Float,AnimationParameterKind.Int=>AnimationPinType.Int,AnimationParameterKind.Bool=>AnimationPinType.Bool,AnimationParameterKind.Trigger=>AnimationPinType.Trigger,_=>AnimationPinType.Pose };
                pins.Add(new(n.Id,n.Kind==AnimationNodeKind.Parameter?"value":"pose",type,true,new((float)n.X+200,(float)n.Y+48)));
            }
        } return pins.ToArray();
    } }
    public Vector2 GraphPoint(Vector2 screen, Vector2 origin) { Finite(screen); Finite(origin); return (screen-origin-Pan)/Zoom; }
    public Vector2 ScreenPoint(Vector2 point, Vector2 origin) => point*Zoom+Pan+origin;
    public Guid? HitNode(Vector2 point) { Finite(point); foreach(var b in Boxes.Reverse()) if(point.X>=b.Position.X&&point.Y>=b.Position.Y&&point.X<=b.Position.X+b.Size.X&&point.Y<=b.Position.Y+b.Size.Y)return b.Id; return null; }
    public AnimationGraphPin? HitPin(Vector2 point) { Finite(point); foreach(var pin in Pins) if(Vector2.Distance(pin.Position,point)*Zoom<=8)return pin;return null; }
    public void TranslatePan(Vector2 delta) { Finite(delta); Pan=Vector2.Clamp(Pan+delta,new(-65536),new(65536)); }
    public void ZoomAt(Vector2 screen,Vector2 origin,float factor) { if(!float.IsFinite(factor)||factor<=0)throw new ArgumentException("Zoom factor."); var before=GraphPoint(screen,origin);Zoom=Math.Clamp(Zoom*factor,.2f,2);Pan=Vector2.Clamp(screen-origin-before*Zoom,new(-65536),new(65536)); }
    public void ResetView() { Zoom=1;Pan=new(24,24); }
    public Guid[] Search(string query) { if(query.Length>128)throw new ArgumentException("Search budget.");return _graph?.Nodes.Where(n=>n.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||n.Kind.ToString().Contains(query,StringComparison.OrdinalIgnoreCase)).Take(32).Select(n=>n.Id).ToArray()??[]; }
    public void Marquee(Vector2 a,Vector2 b,bool additive) { Finite(a);Finite(b);if(!additive)_selection.Clear();var min=Vector2.Min(a,b);var max=Vector2.Max(a,b);foreach(var box in Boxes)if(box.Position.X<=max.X&&box.Position.Y<=max.Y&&box.Position.X+box.Size.X>=min.X&&box.Position.Y+box.Size.Y>=min.Y){if(_selection.Count>=64&&!_selection.Contains(box.Id))break;_selection.Add(box.Id);} }
    public JsonElement Move(Vector2 delta) { Finite(delta); if(_graph is null||_selection.Count==0)throw new ArgumentException("Select nodes.");return AnimationGraphEdits.Operations(_graph.Nodes.Where(n=>_selection.Contains(n.Id)).Select(n=>(object)new {op="node.upsert",node=n with{X=n.X+delta.X,Y=n.Y+delta.Y}}).ToArray()); }
    public JsonElement Connect(AnimationGraphPin from,AnimationGraphPin to,Guid id) { if(id==Guid.Empty||!from.Output||to.Output||from.Node==to.Node||from.Type!=to.Type||!Pins.Contains(from)||!Pins.Contains(to))throw new ArgumentException("Typed graph connection.");var ops=new List<object>();foreach(var l in _graph!.Links.Where(l=>l.To==to.Node&&l.ToPin==to.Name))ops.Add(new{op="link.delete",id=l.Id});ops.Add(new{op="link.upsert",link=new AnimationGraphLink(id,from.Node,from.Name,to.Node,to.Name)});return AnimationGraphEdits.Operations(ops.ToArray()); }
    public JsonElement DeleteSelected() => AnimationGraphEdits.Operations(Selection.Select(id=>(object)new{op="node.delete",id}).ToArray());
    private static void Finite(Vector2 point) { if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||Math.Abs(point.X)>1e7||Math.Abs(point.Y)>1e7)throw new ArgumentException("Bounded canvas coordinates."); }
}
