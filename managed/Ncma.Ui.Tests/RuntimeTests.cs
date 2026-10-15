using System.Numerics;
using Ncma.Ui;

internal static class RuntimeTests
{
    private static void Check(bool value) { if (!value) throw new Exception("UI runtime assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid UI runtime operation accepted."); }
    private static (UiRuntime Runtime, Guid Button, Guid Text, Guid Slider) Runtime()
    {
        var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); Guid button = Guid.NewGuid(), text = Guid.NewGuid(), slider = Guid.NewGuid();
        var b = UiElement.Create(button, "Button", UiKind.Button, d.Root) with { Layout = UiLayout.Fixed(10, 10, 80, 30), Action = "attack" };
        var t = UiElement.Create(text, "Input", UiKind.TextInput, d.Root) with { Order = 1, Layout = UiLayout.Fixed(10, 50, 100, 30), Action = "name.changed", Text = "😀e\u0301" };
        var s = UiElement.Create(slider, "Slider", UiKind.Slider, d.Root) with { Order = 2, Layout = UiLayout.Fixed(10, 90, 100, 20), Action = "volume" };
        var runtime = new UiRuntime(new(d with { Elements = [d.Elements[0], b, t, s] })); runtime.Layout(new(200, 150, 1, default)); return (runtime, button, text, slider);
    }
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Paint-only value/focus changes reuse layout storage and bounded pointer input is allocation-free", () => {
            var (r,button,_,slider)=Runtime();var viewport=new UiViewport(200,150,1,default);var original=r.Layout(viewport);
            r.SetValue(slider,.25f);Check(original.Overlaps(r.Layout(viewport)));
            UiInput[] input=new UiInput[1];var actions=new UiAction[128];ulong sequence=0;
            void Move(int i){input[0]=new(UiInputKind.PointerMove,++sequence,new(i%2==0?40:70,100));r.Input(r.Stamp,input);r.Layout(viewport);r.Drain(r.Stamp,actions);}
            r.Input(r.Stamp,[new(UiInputKind.PointerDown,++sequence,new(60,100))]);r.Drain(r.Stamp,actions);
            for(int i=0;i<32;i++)Move(i);
            long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<256;i++)Move(i);bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(bytes==0&&original.Overlaps(r.Layout(viewport)));
            r.Input(r.Stamp,[new(UiInputKind.PointerUp,++sequence,new(70,100)),new(UiInputKind.Tab,++sequence,default)]);Check(r.Focus==button&&original.Overlaps(r.Layout(viewport)));
            r.CancelInput();Check(original.Overlaps(r.Layout(viewport)));
            r.SetFlags(button,false,false);Reject(()=>r.Input(r.Stamp,[]));Check(!original.Overlaps(r.Layout(viewport)));
            Check(r.HitTest(new(20,20))==Guid.Empty);
            Task.Run(()=>Reject(()=>r.Layout(viewport))).GetAwaiter().GetResult();
        });
        yield return ("UI pointer capture/cancel, keyboard focus, slider and safe copied action drain", () => {
            var (r, button, text, slider) = Runtime(); Guid play = Guid.NewGuid(), world = Guid.NewGuid(); r.Bind(play, world, 10); var stamp = r.Stamp;
            r.Input(stamp, [new(UiInputKind.PointerDown, 1, new(20, 20)), new(UiInputKind.PointerUp, 2, new(150, 140))]); Check(r.QueuedActions == 0 && r.Capture == Guid.Empty);
            r.Input(stamp, [new(UiInputKind.PointerDown, 3, new(20, 20)), new(UiInputKind.PointerUp, 4, new(20, 20))]);
            var actions = new UiAction[128]; Check(r.Drain(stamp, actions) == 1 && actions[0].Action == "attack" && actions[0].Element == button && actions[0].Stamp.World == world);
            Check(r.Drain(stamp, actions) == 0);
            r.Input(stamp, [new(UiInputKind.Tab, 5, default)]); Check(r.Focus == text);
            r.Input(stamp, [new(UiInputKind.PointerDown, 6, new(60, 100)), new(UiInputKind.PointerMove, 7, new(200, 100)), new(UiInputKind.PointerUp, 8, new(200, 100))]);
            Check(r.Widget(slider).Value == 1 && r.Drain(stamp, actions) == 2);
            r.Input(stamp, [new(UiInputKind.FocusLost, 9, default)]); Check(r.Focus == Guid.Empty && r.Capture == Guid.Empty);
        });
        yield return ("Geometry dirtiness still rebuilds scrolling and prepared intrinsic text without mutating borrowed old layouts", () => {
            var d=UiDefinition.Create(Guid.NewGuid(),"Scroll");
            var scroll=UiElement.Create(Guid.NewGuid(),"Scroll",UiKind.ScrollView,d.Root) with{Layout=UiLayout.Fixed(20,20,80,50),Style=UiStyle.Default with{Clip=true}};
            var button=UiElement.Create(Guid.NewGuid(),"Button",UiKind.Button,scroll.Id) with{Layout=UiLayout.Fixed(0,40,40,20),Action="click"};
            var r=new UiRuntime(new(d with{Elements=[d.Elements[0],scroll,button]}));var v=new UiViewport(200,150,1,default);var old=r.Layout(v);
            Check(r.HitTest(new(30,65))==button.Id);r.Input(r.Stamp,[new(UiInputKind.Scroll,1,new(30,30),Delta:20)]);
            var moved=r.Layout(v);Check(!old.Overlaps(moved)&&old[2].Transform.M32==60&&moved[2].Transform.M32==40&&r.HitTest(new(30,45))==button.Id);
            d=UiDefinition.Create(Guid.NewGuid(),"Intrinsic");var root=d.Elements[0] with{Layout=d.Elements[0].Layout with{Flow=UiFlow.Horizontal}};
            var text=UiElement.Create(Guid.NewGuid(),"Text",UiKind.Text,d.Root) with{Text="A",Layout=UiLayout.Fixed(0,0,0,20) with{WidthMode=UiSizing.Hug}};
            button=button with{Parent=d.Root,Order=1,Layout=UiLayout.Fixed(0,0,20,20)};var metrics=new LengthMetrics();
            r=new(new(d with{Elements=[root,text,button]}),metrics);old=r.Layout(v);Check(old[2].Transform.M31==10);
            r.SetText(text.Id,"ABCD");r.PreparedText(text.Id,metrics);Reject(()=>r.Input(r.Stamp,[]));
            moved=r.Layout(v);Check(!old.Overlaps(moved)&&old[2].Transform.M31==10&&moved[2].Transform.M31==40);
            var resized=r.Layout(new(400,300,2,default));Check(!moved.Overlaps(resized)&&resized[2].Transform.M31==40);
        });
        yield return ("UI Unicode grapheme editing and IME proposal/commit/cancel do not run game callbacks", () => {
            var (r, _, id, _) = Runtime(); var stamp = r.Stamp;
            r.Input(stamp, [new(UiInputKind.PointerDown, 1, new(20, 60)), new(UiInputKind.PointerUp, 2, new(20, 60))]);
            r.Input(stamp, [new(UiInputKind.Backspace, 3, default)]); Check(r.Widget(id).Text == "😀");
            r.Input(stamp, [new(UiInputKind.Backspace, 4, default)]); Check(r.Widget(id).Text == "");
            r.Input(stamp, [new(UiInputKind.Composition, 5, default, "中文")]); Check(r.Widget(id).Text == "" && r.Widget(id).Composition == "中文");
            r.Input(stamp, [new(UiInputKind.CompositionCancel, 6, default)]); Check(r.Widget(id).Composition == "");
            r.Input(stamp, [new(UiInputKind.Composition, 7, default, "中"), new(UiInputKind.Text, 8, default, "中")]);
            Check(r.Widget(id).Text == "中" && r.Widget(id).Composition == "");
            var actions = new UiAction[128]; Check(r.Drain(stamp, actions) == 3);
            Reject(() => r.Input(stamp, [new(UiInputKind.Text, 9, default, "\uD800")])); Check(r.Widget(id).Text == "中");
        });
        yield return ("UI stale/replayed inputs, identity changes, bounded queues and complete drain reject", () => {
            var (r, _, _, _) = Runtime(); var stamp = r.Stamp;
            r.Input(stamp, [new(UiInputKind.PointerDown, 1, new(20, 20)), new(UiInputKind.PointerUp, 2, new(20, 20))]);
            Reject(() => r.Input(stamp, [new(UiInputKind.PointerUp, 2, new(20, 20))]));
            Reject(() => r.Input(stamp with { Generation = stamp.Generation + 1 }, [new(UiInputKind.Activate, 3, default)]));
            Reject(() => r.Drain(stamp, Array.Empty<UiAction>())); Check(r.QueuedActions == 1);
            for (ulong i = 3; i <= 129; i++) r.Input(stamp, [new(UiInputKind.Activate, i, default)]);
            Check(r.QueuedActions == 128); Reject(() => r.Input(stamp, [new(UiInputKind.Activate, 130, default)])); Check(r.QueuedActions == 128);
            r.Input(stamp, [new(UiInputKind.FocusLost, 131, default)]); Check(r.Focus == Guid.Empty && r.QueuedActions == 128);
            r.Bind(Guid.NewGuid(), Guid.NewGuid(), 0); Check(r.QueuedActions == 0); Reject(() => r.Drain(stamp, new UiAction[128]));
        });
        yield return ("UI dynamic C# presentation flags, rounded hit, hidden flow and revoked queued actions", () => {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD"); var root = d.Elements[0] with { Layout = d.Elements[0].Layout with { Flow = UiFlow.Vertical } };
            var a = UiElement.Create(Guid.NewGuid(), "A", UiKind.Button, d.Root) with { Action = "a", Layout = UiLayout.Fixed(0, 0, 40, 40), Style = UiStyle.Default with { CornerRadius = 20 } };
            var b = UiElement.Create(Guid.NewGuid(), "B", UiKind.Button, d.Root) with { Order = 1, Action = "b", Layout = UiLayout.Fixed(0, 0, 40, 40) };
            var r = new UiRuntime(new(d with { Elements = [root, a, b] })); r.Layout(new(100, 100, 1, default));
            Check(r.HitTest(new(1, 1)) == Guid.Empty && r.HitTest(new(20, 20)) == a.Id);
            r.Input(r.Stamp, [new(UiInputKind.PointerDown, 1, new(20, 20)), new(UiInputKind.PointerUp, 2, new(20, 20))]); Check(r.QueuedActions == 1);
            r.SetFlags(a.Id, false, false); Check(r.QueuedActions == 0);
            var boxes = r.Layout(new(100, 100, 1, default)); Check(boxes.Length == 2 && boxes[1].Id == b.Id && boxes[1].Transform.M32 == 0);
            r.SetText(b.Id, "New"); Check(r.Text(b.Id) == "New" && d.Elements[0].Text.Length == 0);
        });
    }
    private sealed class LengthMetrics:IUiTextMetrics{public Vector2 Measure(UiElement element,float width)=>new(element.Text.Length*10,20);}
}
