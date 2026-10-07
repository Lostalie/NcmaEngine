using System.Numerics;
using Ncma.Ui;

namespace Ncma.Editor.Services;

public enum UiCanvasTool { Move,Resize,Rotate }
public enum UiResizeHandle { NorthWest,North,NorthEast,East,SouthEast,South,SouthWest,West }
public readonly record struct UiSnapGuide(Vector2 Start,Vector2 End);
// Authoring geometry uses the SAME prepared runtime boxes/clip/matrices, not widget-only hit test.
public sealed class UiCanvasController
{
    private UiDefinition? _definition; private Dictionary<Guid,UiElement> _elements=[];
    private UiLayoutBox[] _boxes=[]; private readonly HashSet<Guid> _selection=[];
    private readonly List<UiSnapGuide> _guides=[];
    public UiSnapGuide[] Guides=>_guides.ToArray();
    public Vector2 Pan { get; private set; } public float Zoom { get; private set; }=1;
    public UiCanvasTool Tool { get; set; }
    public UiResizeHandle ResizeHandle { get; set; }=UiResizeHandle.SouthEast;
    public Guid[] Selection=>_selection.ToArray();
    public void PruneSelection(IEnumerable<Guid> retained){var valid=retained.ToHashSet();_selection.RemoveWhere(id=>!valid.Contains(id));}
    public void Load(UiDefinition definition,ReadOnlySpan<UiLayoutBox> boxes)
    {
        UiCodec.Validate(definition);
        if(_definition?.AssetId!=definition.AssetId)_selection.Clear();
        _definition=UiCodec.Copy(definition);_elements=_definition.Elements.ToDictionary(e=>e.Id);_boxes=boxes.ToArray();_selection.RemoveWhere(id=>!_elements.ContainsKey(id));
    }
    public void Select(Guid id,bool toggle=false,bool append=false)
    {
        if(!_elements.ContainsKey(id))throw new ArgumentException("Missing UI selection.");
        if(!toggle&&!append)_selection.Clear();
        if(toggle&&_selection.Remove(id))return;if(_selection.Count>=64&&!_selection.Contains(id))throw new ArgumentException("UI selection budget.");_selection.Add(id);
    }
    public Guid Hit(Vector2 point)
    {
        Finite(point);
        foreach(var box in _boxes.Reverse()) {
            if(box.Opacity<=0||!box.Clip.Contains(point)||Locked(box.Id)||!Matrix3x2.Invert(box.Transform,out var inverse))continue;
            if(box.Rect.Contains(Vector2.Transform(point,inverse)))return box.Id;
        }
        return Guid.Empty;
    }
    public void Marquee(UiRect rectangle,bool append)
    {
        if(!float.IsFinite(rectangle.X+rectangle.Y+rectangle.Width+rectangle.Height)||rectangle.Width<0||rectangle.Height<0)throw new ArgumentException("UI marquee bounds.");
        if(!append)_selection.Clear();
        foreach(var box in _boxes.Where(b=>b.Id!=_definition!.Root&&b.Opacity>0&&!Locked(b.Id))) {
            var bounds=UiRect.Intersect(Bounds(box),box.Clip);if(UiRect.Intersect(bounds,rectangle).Width>0&&UiRect.Intersect(bounds,rectangle).Height>0)Select(box.Id,append:true);
        }
    }
    public bool Locked(Guid id)
    {
        for(var e=_elements[id];;e=_elements[e.Parent]) { if(e.Locked)return true;if(e.Parent==Guid.Empty)return false; }
    }
    public Vector2 DocumentPoint(Vector2 screen,Vector2 origin){Finite(screen);Finite(origin);return (screen-origin-Pan)/Zoom;}
    public void Translate(Vector2 delta){Finite(delta);Pan=Vector2.Clamp(Pan+delta,new(-65536),new(65536));}
    public void ZoomAt(float factor,Vector2 screen,Vector2 origin,float maximum=8)
    {
        if(!float.IsFinite(factor)||factor<=0||!float.IsFinite(maximum)||maximum<.1f||maximum>8)throw new ArgumentException("UI zoom factor.");var point=DocumentPoint(screen,origin);Zoom=Math.Clamp(Zoom*factor,.1f,maximum);Pan=Vector2.Clamp(screen-origin-point*Zoom,new(-65536),new(65536));
    }
    public void Fit(float width,float height,float documentWidth,float documentHeight)
    {
        if(!float.IsFinite(width+height+documentWidth+documentHeight)||Math.Min(Math.Min(width,height),Math.Min(documentWidth,documentHeight))<=0)throw new ArgumentException("UI fit bounds.");
        Zoom=Math.Clamp(Math.Min(width/documentWidth,height/documentHeight),.1f,8);Pan=new((width-documentWidth*Zoom)/2,(height-documentHeight*Zoom)/2);
    }
    public void ResetView(){Pan=Vector2.Zero;Zoom=1;}
    public void Focus(float width,float height)
    {
        var boxes=_boxes.Where(b=>_selection.Contains(b.Id)).Select(Bounds).ToArray();if(boxes.Length==0)return;
        float x=boxes.Min(b=>b.X),y=boxes.Min(b=>b.Y),w=boxes.Max(b=>b.X+b.Width)-x,h=boxes.Max(b=>b.Y+b.Height)-y;
        Fit(width,height,Math.Max(w,1)+32,Math.Max(h,1)+32);Pan-=new Vector2(x-16,y-16)*Zoom;
    }
    public UiEdit[] Transform(Vector2 documentDelta,bool snap=true)
    {
        Finite(documentDelta);var result=new List<UiEdit>();var selected=TopSelection();_guides.Clear();var movement=new Dictionary<Guid,Vector2>();
        foreach(var e in selected) {
            Movable(e);var parent=_boxes.Single(b=>b.Id==e.Parent);
            if(!Matrix3x2.Invert(parent.Transform,out var inverse))throw new ArgumentException("Singular UI parent.");
            var delta=Vector2.TransformNormal(documentDelta,inverse);var l=e.Layout;
            if(Tool==UiCanvasTool.Move){if(!movement.TryGetValue(e.Parent,out var shared)){shared=snap?SnapMovement(e.Parent,selected.Where(s=>s.Parent==e.Parent).ToArray(),parent,inverse,delta):delta;movement.Add(e.Parent,shared);}delta=shared;}
            UiLayout candidate=Tool switch {
                UiCanvasTool.Move=>l with{X=l.X+delta.X,Y=l.Y+delta.Y},
                UiCanvasTool.Resize=>Resize(l,delta),
                UiCanvasTool.Rotate=>l with{Rotation=Math.Clamp(snap?MathF.Round((l.Rotation+documentDelta.X)/15)*15:l.Rotation+documentDelta.X,-360,360)},
                _=>throw new ArgumentException("UI tool.") };
            result.Add(new(UiEditKind.Replace,e.Id,e with{Layout=candidate}));
        }
        return result.ToArray();
        float Snap(float x)=>snap&&Math.Abs(x-MathF.Round(x/8)*8)*Zoom<=6?MathF.Round(x/8)*8:x;
        UiLayout Resize(UiLayout l,Vector2 delta){
            bool left=ResizeHandle is UiResizeHandle.NorthWest or UiResizeHandle.SouthWest or UiResizeHandle.West;
            bool right=ResizeHandle is UiResizeHandle.NorthEast or UiResizeHandle.SouthEast or UiResizeHandle.East;
            bool top=ResizeHandle is UiResizeHandle.NorthWest or UiResizeHandle.North or UiResizeHandle.NorthEast;
            bool bottom=ResizeHandle is UiResizeHandle.SouthWest or UiResizeHandle.South or UiResizeHandle.SouthEast;
            if((left||right)&&l.WidthMode!=UiSizing.Fixed||(top||bottom)&&l.HeightMode!=UiSizing.Fixed)throw new ArgumentException("Selected Hug/Fill resize axis is layout controlled.");
            var rotation=Matrix3x2.CreateRotation(l.Rotation*MathF.PI/180);var local=Vector2.TransformNormal(delta,Matrix3x2.CreateRotation(-l.Rotation*MathF.PI/180));
            float width=Math.Clamp(left?Snap(l.Width-local.X):right?Snap(l.Width+local.X):l.Width,Math.Max(1,l.MinWidth),l.MaxWidth);
            float height=Math.Clamp(top?Snap(l.Height-local.Y):bottom?Snap(l.Height+local.Y):l.Height,Math.Max(1,l.MinHeight),l.MaxHeight);
            var centerDelta=new Vector2((l.Width-width)/2,(l.Height-height)/2);var origin=new Vector2(left?l.Width-width:0,top?l.Height-height:0);
            var shift=centerDelta+Vector2.TransformNormal(origin-centerDelta,rotation);
            return l with{X=l.X+shift.X+l.AnchorX*(width-l.Width),Y=l.Y+shift.Y+l.AnchorY*(height-l.Height),Width=width,Height=height};
        }
    }
    private Vector2 SnapMovement(Guid parentId,UiElement[] selected,UiLayoutBox parent,Matrix3x2 inverse,Vector2 delta)
    {
        var ids=selected.Select(e=>e.Id).ToHashSet();var bounds=selected.Select(e=>LocalBounds(_boxes.Single(b=>b.Id==e.Id),inverse)).ToArray();
        float x=bounds.Min(b=>b.X),y=bounds.Min(b=>b.Y),width=bounds.Max(b=>b.X+b.Width)-x,height=bounds.Max(b=>b.Y+b.Height)-y;
        var siblings=_boxes.Where(b=>_elements[b.Id].Parent==parentId&&!ids.Contains(b.Id)&&b.Opacity>0&&!Locked(b.Id)).Select(b=>LocalBounds(b,inverse)).ToArray();
        delta.X=Axis(true,x,width,delta.X,parent.Rect.Width);delta.Y=Axis(false,y,height,delta.Y,parent.Rect.Height);return delta;
        float Axis(bool horizontal,float origin,float extent,float shift,float parentExtent){
            float scale=Vector2.TransformNormal(horizontal?Vector2.UnitX:Vector2.UnitY,parent.Transform).Length()*Zoom;
            float[] edges=[origin+shift,origin+extent/2+shift,origin+extent+shift];
            // Parent guides precede peer guides, then the grid. A group's correction
            // is shared by its members, preserving spacing and relative transforms.
            foreach(var targets in new[]{new[]{0f,parentExtent/2,parentExtent},siblings.SelectMany(b=>horizontal?new[]{b.X,b.X+b.Width/2,b.X+b.Width}:new[]{b.Y,b.Y+b.Height/2,b.Y+b.Height}).ToArray()}){
                float best=float.PositiveInfinity,correction=0,target=0;
                foreach(float t in targets)foreach(float edge in edges)if(Math.Abs(t-edge)*scale<=6&&Math.Abs(t-edge)<best){best=Math.Abs(t-edge);correction=t-edge;target=t;}
                if(float.IsFinite(best)){Vector2 a=horizontal?new(target,0):new(0,target),b=horizontal?new(target,parent.Rect.Height):new(parent.Rect.Width,target);_guides.Add(new(Vector2.Transform(a,parent.Transform),Vector2.Transform(b,parent.Transform)));return shift+correction;}
            }
            float grid=MathF.Round((origin+shift)/8)*8;return Math.Abs(grid-origin-shift)*scale<=6?grid-origin:shift;
        }
    }
    private static UiRect LocalBounds(UiLayoutBox box,Matrix3x2 inverse){Vector2[] corners=[Vector2.Zero,new(box.Rect.Width,0),new(box.Rect.Width,box.Rect.Height),new(0,box.Rect.Height)];var p=corners.Select(v=>Vector2.Transform(Vector2.Transform(v,box.Transform),inverse)).ToArray();return new(p.Min(v=>v.X),p.Min(v=>v.Y),p.Max(v=>v.X)-p.Min(v=>v.X),p.Max(v=>v.Y)-p.Min(v=>v.Y));}
    public UiResizeHandle? HandleAt(Vector2 point){
        if(_selection.Count!=1)return null;var box=_boxes.SingleOrDefault(b=>b.Id==_selection.Single());if(box.Id==Guid.Empty)return null;
        Vector2[] p=[new(0,0),new(box.Rect.Width/2,0),new(box.Rect.Width,0),new(box.Rect.Width,box.Rect.Height/2),new(box.Rect.Width,box.Rect.Height),new(box.Rect.Width/2,box.Rect.Height),new(0,box.Rect.Height),new(0,box.Rect.Height/2)];
        for(int i=0;i<p.Length;i++)if(Vector2.Distance(Vector2.Transform(p[i],box.Transform),point)*Zoom<=8)return (UiResizeHandle)i;return null;
    }
    private static UiLayout FixedAxes(UiLayout l) { if(l.WidthMode!=UiSizing.Fixed||l.HeightMode!=UiSizing.Fixed)throw new ArgumentException("Hug/Fill axes are layout controlled; explicitly choose Fixed first.");return l; }
    private void Movable(UiElement e)
    {
        if(e.Id==_definition!.Root||Locked(e.Id))throw new ArgumentException("UI root/locked transform denied.");
        if(_elements[e.Parent].Layout.Flow!=UiFlow.Free)throw new ArgumentException("Auto-layout transform denied; use layer order.");
    }
    public UiEdit[] Align(bool horizontal,bool distribute=false)
    {
        var selected=TopSelection().ToArray();SameSpace(selected);if(selected.Length<(distribute?3:2))throw new ArgumentException("UI alignment selection.");
        foreach(var e in selected){Movable(e);FixedAxes(e.Layout);if(e.Layout.Rotation!=0||e.Layout.AnchorX!=0||e.Layout.AnchorY!=0)throw new ArgumentException("Alignment requires unrotated/unanchored same-space elements.");}
        var ordered=selected.OrderBy(e=>horizontal?e.Layout.X:e.Layout.Y).ToArray();float minimum=horizontal?ordered[0].Layout.X:ordered[0].Layout.Y;
        float maximum=ordered.Max(e=>horizontal?e.Layout.X+e.Layout.Width:e.Layout.Y+e.Layout.Height);
        float extent=ordered.Sum(e=>horizontal?e.Layout.Width:e.Layout.Height),cursor=minimum;
        var result=new List<UiEdit>();foreach(var e in ordered){var l=e.Layout;result.Add(new(UiEditKind.Replace,e.Id,e with{Layout=horizontal?l with{X=distribute?cursor:minimum}:l with{Y=distribute?cursor:minimum}}));if(distribute)cursor+=(horizontal?l.Width:l.Height)+(maximum-minimum-extent)/(ordered.Length-1);}
        return result.ToArray();
    }
    public UiEdit[] Group()
    {
        var selected=TopSelection().ToArray();SameSpace(selected);if(selected.Length<2)throw new ArgumentException("Select at least two UI elements.");
        foreach(var e in selected){Movable(e);FixedAxes(e.Layout);if(e.Layout.Rotation!=0||e.Layout.AnchorX!=0||e.Layout.AnchorY!=0)throw new ArgumentException("Grouping cannot preserve this geometry.");}
        var siblings=_elements.Values.Where(e=>e.Parent==selected[0].Parent).OrderBy(e=>e.Order).ToArray();int first=Array.FindIndex(siblings,e=>selected.Any(s=>s.Id==e.Id)),last=Array.FindLastIndex(siblings,e=>selected.Any(s=>s.Id==e.Id));
        if(last-first+1!=selected.Length)throw new ArgumentException("Grouping interleaved layers changes draw order; select contiguous siblings.");
        float x=selected.Min(e=>e.Layout.X),y=selected.Min(e=>e.Layout.Y),w=selected.Max(e=>e.Layout.X+e.Layout.Width)-x,h=selected.Max(e=>e.Layout.Y+e.Layout.Height)-y;
        Guid id=Guid.NewGuid();int order=selected.Min(e=>e.Order);
        var group=UiElement.Create(id,"Group",UiKind.Group,selected[0].Parent) with{Order=order,Layout=UiLayout.Fixed(x,y,w,h)};
        var result=new List<UiEdit>{new(UiEditKind.Add,id,group)};
        int childOrder=0;foreach(var e in selected.OrderBy(e=>e.Order))result.Add(new(UiEditKind.Replace,e.Id,e with{Parent=id,Order=childOrder++,Layout=e.Layout with{X=e.Layout.X-x,Y=e.Layout.Y-y}}));return result.ToArray();
    }
    public UiEdit[] Order(Guid id,int delta)
    {
        var e=_elements[id];if(e.Id==_definition!.Root||Locked(id)||delta is not (-1 or 1))throw new ArgumentException("UI layer order.");
        var siblings=_elements.Values.Where(i=>i.Parent==e.Parent).OrderBy(i=>i.Order).ToList();int at=siblings.FindIndex(i=>i.Id==id),to=Math.Clamp(at+delta,0,siblings.Count-1);
        (siblings[at],siblings[to])=(siblings[to],siblings[at]);return siblings.Select((i,index)=>new UiEdit(UiEditKind.Replace,i.Id,i with{Order=index})).ToArray();
    }
    public bool AutoLayout(Guid id)=>_elements[id].Parent!=Guid.Empty&&_elements[_elements[id].Parent].Layout.Flow!=UiFlow.Free;
    public UiEdit[] ReorderAt(Guid id,Vector2 point){
        Finite(point);var e=_elements[id];if(!AutoLayout(id)||Locked(id))throw new ArgumentException("Auto-layout reorder requires an unlocked child.");
        var parent=_elements[e.Parent];var siblings=_elements.Values.Where(i=>i.Parent==e.Parent&&i.Id!=id).OrderBy(i=>i.Order).ToList();
        if(siblings.Count>=UiEdits.MaxOperations)throw new ArgumentException("UI reorder batch exceeds operation budget.");
        var box=_boxes.Single(b=>b.Id==parent.Id);if(!Matrix3x2.Invert(box.Transform,out var inverse))throw new ArgumentException("Singular UI parent.");var local=Vector2.Transform(point,inverse);float axis=parent.Layout.Flow==UiFlow.Horizontal?local.X:local.Y;int index=0;
        foreach(var sibling in siblings){var sbox=_boxes.FirstOrDefault(b=>b.Id==sibling.Id);if(sbox.Id==Guid.Empty)continue;var center=Vector2.Transform(Vector2.Transform(new(sbox.Rect.Width/2,sbox.Rect.Height/2),sbox.Transform),inverse);if(axis<(parent.Layout.Flow==UiFlow.Horizontal?center.X:center.Y))break;index++;}
        siblings.Insert(index,e);return siblings.Select((s,order)=>new UiEdit(UiEditKind.Replace,s.Id,s with{Order=order})).ToArray();
    }
    public UiEdit[] Delete(Guid confirmed)
    {
        var selection=TopSelection().ToArray();if(selection.Length!=1||selection[0].Id!=confirmed||confirmed==_definition!.Root||Locked(confirmed))throw new ArgumentException("Confirm exact unlocked UI subtree UUID.");
        return [new(UiEditKind.RemoveSubtree,confirmed)];
    }
    public UiElement[] TopSelection()=>_selection.Select(id=>_elements[id]).Where(e=>!HasSelectedAncestor(e)).ToArray();
    private bool HasSelectedAncestor(UiElement e){while(e.Parent!=Guid.Empty){if(_selection.Contains(e.Parent))return true;e=_elements[e.Parent];}return false;}
    private static void SameSpace(UiElement[] selected){if(selected.Length==0||selected.Select(e=>e.Parent).Distinct().Count()!=1)throw new ArgumentException("Same parent UI space required.");}
    public static UiRect Bounds(UiLayoutBox box)
    {
        Vector2[] points=[Vector2.Zero,new(box.Rect.Width,0),new(0,box.Rect.Height),new(box.Rect.Width,box.Rect.Height)];var p=points.Select(v=>Vector2.Transform(v,box.Transform)).ToArray();
        return new(p.Min(v=>v.X),p.Min(v=>v.Y),p.Max(v=>v.X)-p.Min(v=>v.X),p.Max(v=>v.Y)-p.Min(v=>v.Y));
    }
    private static void Finite(Vector2 value){if(!float.IsFinite(value.X)||!float.IsFinite(value.Y))throw new ArgumentException("UI finite point required.");}
    public static bool ClipSegment(ref Vector2 a,ref Vector2 b,UiRect clip)
    {
        Finite(a);Finite(b);float enter=0,leave=1;var delta=b-a;
        if(!Edge(-delta.X,a.X-clip.X)||!Edge(delta.X,clip.X+clip.Width-a.X)||!Edge(-delta.Y,a.Y-clip.Y)||!Edge(delta.Y,clip.Y+clip.Height-a.Y))return false;
        var start=a;a=start+enter*delta;b=start+leave*delta;return true;
        bool Edge(float p,float q){if(p==0)return q>=0;float t=q/p;if(p<0){if(t>leave)return false;enter=Math.Max(enter,t);}else{if(t<enter)return false;leave=Math.Min(leave,t);}return true;}
    }
}
