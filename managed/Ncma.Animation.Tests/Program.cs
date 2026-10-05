using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Gameplay;

if(args.Length is not (1 or 3))throw new ArgumentException("Pose kernel path, optional repository/configuration required.");
static void Check(bool value){if(!value)throw new Exception("Animation assertion failed.");}
static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return;}throw new Exception("Invalid animation operation accepted.");}
static bool Near(Matrix4x4 a,Matrix4x4 b){ReadOnlySpan<Matrix4x4> x=[a],y=[b];var xs=MemoryMarshal.Cast<Matrix4x4,float>(x);var ys=MemoryMarshal.Cast<Matrix4x4,float>(y);for(int i=0;i<16;i++)if(Math.Abs(xs[i]-ys[i])>1e-4f+Math.Abs(ys[i])*1e-5f)return false;return true;}
static Matrix4x4 Model(ImportTransform t)=>Matrix4x4.CreateScale(t.Scale)*Matrix4x4.CreateFromQuaternion(t.Rotation)*Matrix4x4.CreateTranslation(t.Position);
var cases=new List<(string,Action)>();
Guid id=Guid.NewGuid();var settings=new ClipPlaybackData(id,true,false,1,0);
cases.Add(("Pure managed core and persistent scalar component",()=>{
 Check(!typeof(ClipClock).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.Contains("Native")||a.Name.Contains("Python")));
 var world=new World(components:ClipPlaybackData.Register(ComponentRegistry.CreateDefault()));var obj=world.CreateObject("clip");Guid objectId=obj.PersistentId;obj.Set(settings);string bytes=world.SerializeSnapshot();world.LoadSnapshot(bytes);Check(world.FindObject(objectId).Get<ClipPlaybackData>()==settings);Reject(()=>ClipPlaybackData.Validate(settings with{Speed=double.NaN}));Reject(()=>ClipPlaybackData.Validate(settings with{ClipId=Guid.Empty}));
}));
cases.Add(("Clock follows successful ticks; failed step does not advance",()=>{
 var world=new World();var clock=new ClipClock(world,settings,1);var runner=new WorldRunner(world,.1);runner.AddCommittedObserver(new Observer((w,h)=>clock.ObserveCommitted(w,settings,1,h)));
 runner.AddSystem(new SystemStep((w,h)=>{if(w.Tick==1)throw new InvalidOperationException("injected failed simulation step");}));Reject(()=>runner.Advance(.2));
 Check(world.Tick==1&&clock.CommittedTick==1&&Math.Abs(clock.UnwrappedTime-.1)<1e-12&&runner.LastStepsExecuted==1&&runner.IsFaulted);
}));
cases.Add(("Clock rejects attempted observation inside an uncommitted step",()=>{
 var world=new World();var clock=new ClipClock(world,settings,1);var runner=new WorldRunner(world,.1);runner.AddSystem(new SystemStep((w,h)=>clock.ObserveCommitted(w,settings with{StartTime=.5},1,h)));
 Reject(()=>runner.Advance(.1));Check(world.Tick==0&&clock.CommittedTick==0&&clock.UnwrappedTime==0);
}));
cases.Add(("Post-commit observers cannot mutate World; successful tick remains",()=>{
 var world=new World();var obj=world.CreateObject("readonly");obj.Set(TransformData.Identity);var runner=new WorldRunner(world,.1);
 runner.AddCommittedObserver(new Observer((w,h)=>w.FindObject(obj.PersistentId).Set(TransformData.Identity with{Position=Vector3.One})));
 Reject(()=>runner.Advance(.1));Check(world.Tick==1&&runner.LastStepsExecuted==1&&obj.Get<TransformData>().Position==Vector3.Zero&&runner.IsFaulted);
}));
cases.Add(("30/60/144Hz render has identical committed time",()=>{
 foreach(int hz in new[]{30,60,144}){var w=new World();var clock=new ClipClock(w,settings,2);var runner=new WorldRunner(w);runner.AddCommittedObserver(new Observer((v,h)=>clock.ObserveCommitted(v,settings,2,h)));
  for(int frame=0;frame<hz;frame++){runner.Advance(1.0/hz);var t=clock.Sample((float)(runner.Accumulator*60),false);Check(t.Current>=t.Previous);}
  Check(w.Tick==60&&Math.Abs(clock.UnwrappedTime-1)<1e-10);}
}));
cases.Add(("Clock pause/speed/exact endpoint/loop and owner",()=>{
 var w=new World();var clock=new ClipClock(w,settings,1);var runner=new WorldRunner(w,.1);runner.AddCommittedObserver(new Observer((v,h)=>clock.ObserveCommitted(v,settings,1,h)));
 runner.Advance(.1);var before=clock.Sample(.5f,false);clock.ObserveCommitted(w,settings,1,.1);Check(clock.Sample(.5f,false)==before);
 settings=settings with{Playing=false};runner.Advance(.2);Check(Math.Abs(clock.UnwrappedTime-.1)<1e-12&&clock.Sample(0,false).Alpha==1);
 settings=settings with{Playing=true,Speed=2};runner.Advance(.4);Check(Math.Abs(clock.UnwrappedTime-.9)<1e-12);runner.Advance(.1);Check(clock.Sample(1,false).Current==1);
 settings=settings with{Loop=true};runner.Advance(.1);Check(Math.Abs(clock.Sample(0,true).Current-.2)<1e-12&&clock.Sample(0,true).Alpha==1);
 Check(Task.Run(()=>{try{clock.Sample(1,false);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
 Reject(()=>clock.ObserveCommitted(new World(),settings,1,.1));settings=new(id,true,false,1,0);
}));
cases.Add(("Play Pause/Resume/Step and successful-step observation",()=>{
 var doc=new SceneDocument();var world=doc.World;using var play=new PlaySession(doc);var clock=new ClipClock(world,settings,2);play.AddCommittedObserver(new Observer((w,h)=>clock.ObserveCommitted(w,settings,2,h)));play.Start(_=>throw new Exception("no behaviours"));
 play.Pause();play.AdvanceFrame(1);Check(clock.CommittedTick==0);play.Step();Check(clock.CommittedTick==1);play.Resume();play.AdvanceFrame(1.0/60);Check(clock.CommittedTick==2);play.Stop();
}));
cases.Add(("Post-commit fault reports successful steps for Player fixed and paused single-step",()=>{
 foreach(bool headless in new[]{false,true}){
  var doc=new SceneDocument();using var play=new PlaySession(doc,policy:headless?FrameTimePolicy.Strict:FrameTimePolicy.Interactive,advanceMode:headless?PlayAdvanceMode.FixedSteps:PlayAdvanceMode.Frames);
  play.AddCommittedObserver(new Observer((w,h)=>throw new InvalidOperationException("post-commit fixture")));play.Start(_=>throw new Exception("no behaviours"));
  PlayStatus status;if(headless)status=play.AdvanceFixedStep();else{play.Pause();status=play.Step();}
  Check(status.Tick==1&&status.StepsExecuted==1&&status.State==PlayState.Faulted);play.Stop();
 }
}));
string path=Path.GetFullPath(args[0]);string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
cases.Add(("Code pin uses owned file handle, rejects hard links and releases locks",()=>{
 string directory=Path.Combine(args.Length==3?Path.Combine(Path.GetFullPath(args[1]),"out","verification","m3-5",args[2]):Path.GetTempPath(),"code-pin-"+Guid.NewGuid().ToString("N"));
 Directory.CreateDirectory(directory);string file=Path.Combine(directory,"NcmaAnimationKernel.dll"),moved=Path.Combine(directory,"released.dll");File.WriteAllText(file,"isolated code pin fixture");
 using(var pin=new KernelCodePin(file)){
  Check(SHA256.HashData(pin.Stream).SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))));
  bool locked=false;try{using var writer=File.Open(file,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);}catch(IOException){locked=true;}Check(locked);
  locked=false;try{File.Move(file,moved);}catch(IOException){locked=true;}Check(locked&&File.Exists(file)&&!File.Exists(moved));
 }
 File.Move(file,moved);File.Copy(moved,file);string link=Path.Combine(directory,"hardlink.dll");Check(PinFixture.CreateHardLinkW(link,file,0));
 bool rejected=false;try{using var invalid=new KernelCodePin(file);}catch(IOException){rejected=true;}Check(rejected);
 // Failed pin must release all handles. Rename only this fixture; retain outputs for inspection.
 File.Move(file,Path.Combine(directory,"hardlink-released.dll"));
}));
using var kernel=new PoseKernel(path,hash);
var root=new ImportTransform(new(1,2,3),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f),new(1.5f));
var helper=new ImportTransform(new(0,1,0),Quaternion.CreateFromAxisAngle(Vector3.UnitX,.2f),Vector3.One);
var skeleton=new SkeletonPayload([new("root",-1,root),new("helper",0,helper)]);
using var rig=kernel.CreateRig(skeleton);
var target=root with{Position=new(3,4,5),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,1.4f)};
using var clip=kernel.CreateClip(rig,new(2,new("clip",1,[new(0,[new(0,root),new(1,target)])])));
var local=new PoseTrs[4];var models=new Matrix4x4[4];PoseSample[] requests=[new(rig,clip,new(0,1,.5f)),new(rig,null,new(0,0,1))];
cases.Add(("Pose interpolation + helper model independent managed oracle",()=>{
 Check(kernel.Sample(requests,local,models)==4);var middle=new ImportTransform(Vector3.Lerp(root.Position,target.Position,.5f),Quaternion.Slerp(root.Rotation,target.Rotation,.5f),root.Scale);
 Check(Near(models[0],Model(middle))&&Near(models[1],Model(helper)*Model(middle))&&Near(models[2],Model(root)));
}));
cases.Add(("Per-mesh binding palette composition and owned candidate rejection",()=>{
 var vertices=new[]{new ImportVertex(Vector3.Zero,Vector3.UnitZ,Vector2.Zero,default,new(1,0,0,0)),new ImportVertex(Vector3.UnitX,Vector3.UnitZ,Vector2.UnitX,default,new(1,0,0,0)),new ImportVertex(Vector3.UnitY,Vector3.UnitZ,Vector2.UnitY,default,new(1,0,0,0))};
 var source=new MeshPayload(true,2,vertices,[],[0u,1u,2u],[0u],[new(1,AssetMatrices.EncodeColumnMajor(Matrix4x4.CreateTranslation(2,0,0)))],1);
 var other=source with{Bindings=[new(1,AssetMatrices.EncodeColumnMajor(Matrix4x4.CreateTranslation(-3,0,0)))]};var first=new MeshBindingPalette(source,2);var second=new MeshBindingPalette(other,2);kernel.Sample(requests,local,models);
 var a=new Matrix4x4[1];var b=new Matrix4x4[1];first.Compose(models.AsSpan(0,2),a);second.Compose(models.AsSpan(0,2),b);Check(a[0]!=b[0]&&Near(a[0],Matrix4x4.CreateTranslation(2,0,0)*models[1]));
 source.Bindings[0].GeometryToBone[12]=99;first.Compose(models.AsSpan(0,2),a);Check(Near(a[0],Matrix4x4.CreateTranslation(2,0,0)*models[1]));var before=a[0];models[1]=default;Reject(()=>first.Compose(models.AsSpan(0,2),a));Check(a[0]==before);
}));
cases.Add(("Full negative batch, stale/foreign, uniform scale and lifecycle",()=>{
 var before=kernel.Statistics;models[0]=Matrix4x4.Identity;var bad=(PoseSample[])requests.Clone();bad[1]=new(rig,null,new(0,1,1));Reject(()=>kernel.Sample(bad,local,models));Check(models[0]==Matrix4x4.Identity&&kernel.Statistics.SampleCalls==before.SampleCalls);
 Reject(()=>kernel.CreateRig(new([new("bad",-1,root with{Scale=new(1,2,1)})])));
 Reject(()=>kernel.CreateClip(rig,new(1,new("wrong",1,[]))));Reject(()=>rig.Dispose());Reject(()=>kernel.Dispose());
 using var other=new PoseKernel(path,hash);Reject(()=>other.Sample(requests,local,models));
 Check(Task.Run(()=>{try{kernel.Sample(requests,local,models);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult());
}));
cases.Add(("Bounded 32 characters, 32768 bones and zero managed sample allocations",()=>{
 var many=new ImportBone[1024];for(int i=0;i<many.Length;i++)many[i]=new("bone",i-1,new(Vector3.Zero,Quaternion.Identity,Vector3.One));using var large=kernel.CreateRig(new(many));
 var samples=new PoseSample[32];for(int i=0;i<samples.Length;i++)samples[i]=new(large,null,new(0,0,1));var ls=new PoseTrs[32768];var ms=new Matrix4x4[32768];
 for(int i=0;i<4;i++)kernel.Sample(samples,ls,ms);long bytes=GC.GetAllocatedBytesForCurrentThread();long started=System.Diagnostics.Stopwatch.GetTimestamp();
 for(int i=0;i<8;i++)kernel.Sample(samples,ls,ms);double nativeMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
 Check(GC.GetAllocatedBytesForCurrentThread()==bytes&&ms[^1]==Matrix4x4.Identity);Reject(()=>kernel.Sample(new PoseSample[33],ls,ms));
 // Trusted-input bind-matrix baseline only: excludes ABI, finite/whole-batch validation and sampling.
 // This comparison must never be presented as an equal-contract native-vs-managed speedup.
 var baseline=new Matrix4x4[32768];started=System.Diagnostics.Stopwatch.GetTimestamp();
 for(int repeat=0;repeat<8;repeat++)for(int character=0;character<32;character++)for(int bone=0;bone<1024;bone++) {
  int at=character*1024+bone;baseline[at]=bone==0?Matrix4x4.Identity:Matrix4x4.Identity*baseline[at-1];
 }
 double managedMatrixMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;Check(baseline[^1]==ms[^1]);
 if(args.Length==3){string directory=Path.Combine(Path.GetFullPath(args[1]),"out/verification/m3-5",args[2]);Directory.CreateDirectory(directory);
 File.WriteAllText(Path.Combine(directory,"pose-large-profile.json"),System.Text.Json.JsonSerializer.Serialize(new{characters=32,bones=1024,iterations=8,nativeMilliseconds,managedMatrixMilliseconds,
   nativeContract="bounded immutable pose contract with complete validation and atomic copied outputs",managedContract="trusted-input identity bind-matrix baseline only; no clip evaluation/validation/atomic staging",equalContracts=false,nativeAdvantageClaimed=false}));}
}));
if(args.Length==3)foreach(string fixture in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"})cases.Add(("Imported FBX pose/mesh binding CPU oracle "+fixture,()=>{
 string source=Path.Combine(Path.GetFullPath(args[1]),"tests","assets","fbx",fixture),native=Path.GetDirectoryName(path)!;
 string importPath=Path.Combine(native,"NcmaImportKernel.dll");using var importer=new Ncma.Asset.Import.ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));
 var imported=importer.LoadAndCopy(source,30);using var rigResource=kernel.CreateRig(new(imported.Bones));using var cpu=new Ncma.ImportedCharacterResource(Path.Combine(native,"NcmaNative.dll"),source);
 var locals=new PoseTrs[imported.Bones.Length];var globals=new Matrix4x4[locals.Length];var reference=new float[cpu.SampleFloatCount];double max=0;
 for(int c=0;c<imported.Clips.Length;c++){
  var data=imported.Clips[c];using var clipResource=kernel.CreateClip(rigResource,new(imported.Bones.Length,data));
  foreach(double time in new[]{0,data.Duration*.5,data.Duration*(1-1e-8)}){
   kernel.Sample(new[]{new PoseSample(rigResource,clipResource,new(time,time,1))},locals,globals);cpu.Sample(c,time,reference);
   for(int bone=0;bone<globals.Length;bone++){var matrix=globals[bone];ReadOnlySpan<Matrix4x4> row=[matrix];var values=MemoryMarshal.Cast<Matrix4x4,float>(row);for(int k=0;k<16;k++){double error=Math.Abs(values[k]-reference[bone*16+k]);Check(error<=1e-4+Math.Abs(reference[bone*16+k])*1e-5);max=Math.Max(max,error);}}
   int offset=globals.Length*16;
   foreach(var mesh in imported.Meshes){var payload=new MeshPayload(true,imported.Bones.Length,mesh.Vertices,[],mesh.Indices,mesh.TriangleMaterials,mesh.Bindings,mesh.Materials.Length);var bindings=new MeshBindingPalette(payload,globals.Length);var palette=new Matrix4x4[bindings.BindingCount];bindings.Compose(globals,palette);
    foreach(var v in mesh.Vertices){Vector3 position=default;Add(v.Joints.X,v.Weights.X);Add(v.Joints.Y,v.Weights.Y);Add(v.Joints.Z,v.Weights.Z);Add(v.Joints.W,v.Weights.W);for(int axis=0;axis<3;axis++){double expected=reference[offset++],actual=axis==0?position.X:axis==1?position.Y:position.Z;double error=Math.Abs(actual-expected);Check(error<=1e-4+Math.Abs(expected)*1e-5);max=Math.Max(max,error);}void Add(ushort joint,float weight){if(weight!=0)position+=Vector3.Transform(v.Position,palette[joint])*weight;}}
   }
  }
 }
 string directory=Path.Combine(Path.GetFullPath(args[1]),"out","verification","m3-5",args[2]);Directory.CreateDirectory(directory);
 File.WriteAllText(Path.Combine(directory,fixture+".pose.json"),System.Text.Json.JsonSerializer.Serialize(new{fixture,bones=imported.Bones.Length,clips=imported.Clips.Length,cpuCharacterOracleMaxError=max,sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),gpuSkinning=false,g5Accepted=false}));
}));
int passed=0;foreach(var (name,run)in cases){try{run();Console.WriteLine("PASS "+name);passed++;}catch(Exception e){Console.Error.WriteLine("FAIL "+name+"\n"+e);return 1;}}
clip.Dispose();rig.Dispose();Check(kernel.Statistics.Rigs==0&&kernel.Statistics.Clips==0&&kernel.Statistics.RetainedBytes==0);Console.WriteLine($"Animation pose/clock candidate: {passed}/{cases.Count} passed; GPU/G5 not accepted.");return 0;
sealed class Observer(Action<World,double> run):ICommittedStepObserver{public void StepCommitted(World world,double fixedDeltaSeconds)=>run(world,fixedDeltaSeconds);}
sealed class SystemStep(Action<World,double> run):IWorldSystem{public void FixedUpdate(World world,double fixedDeltaSeconds)=>run(world,fixedDeltaSeconds);}
static class PinFixture{
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool CreateHardLinkW(string path,string existing,nint security);
}
