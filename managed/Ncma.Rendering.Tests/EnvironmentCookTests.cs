using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int EnvironmentChild(string path,string expected)
    {
        var package=EnvironmentPackage.Decode(File.ReadAllBytes(path),expected);
        Check(!package.GpuValidated&&Process.GetCurrentProcess().Modules.Cast<ProcessModule>().All(m=>m.ModuleName is not ("NcmaRenderer.dll" or "NcmaPlatform.dll" or "NcmaGui.dll")),"Source-free environment preflight loaded native plugins.");
        return 0;
    }
    private static int EnvironmentCookTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0;double directionError=0,lutError=0,constantError=0,energy=0;bool allowed=true;
        void Pass(bool value,string message){Check(value,"M7.3-A "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
        HdrEnvironmentSource Source(Guid id,Vector3 center,Vector3 gradient){
            const int width=128,height=64;float[] rgba=new float[width*height*4];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){double phi=2*Math.PI*((x+.5)/width-.5),theta=Math.PI*(y+.5)/height;float dx=(float)(Math.Sin(theta)*Math.Cos(phi));
                var c=center+gradient*dx;int at=(y*width+x)*4;rgba[at]=c.X;rgba[at+1]=c.Y;rgba[at+2]=c.Z;rgba[at+3]=1;}
            return HdrEnvironmentSource.Prepare(id,width,height,rgba);
        }
        Vector3 Direction(int face,int x,int y,int size){
            float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;
            return Vector3.Normalize(face switch{0=>new(1,-v,-u),1=>new(-1,-v,u),2=>new(u,1,v),3=>new(u,-1,-v),4=>new(u,-v,1),_=>new(-u,-v,-1)});
        }
        var settings=new EnvironmentCookSettings(8,8,16,1024);settings.Validate();
        Pass(settings.Levels==4&&settings.FloatCount<EnvironmentPackage.MaxBytes/4,"bounded layout");
        Bad(()=>new EnvironmentCookSettings(3,8,16,1024).Validate());Bad(()=>new EnvironmentCookSettings(64,16,64,2048).Validate());
        Bad(()=>new EnvironmentCookSettings(8,8,16,63).Validate());Bad(()=>new EnvironmentCookSettings(8,8,128,64).Validate());
        var neutral=HdrEnvironmentSource.Neutral(Guid.NewGuid());Pass(neutral.Pixels[0]==.18f&&neutral.Pixels[3]==1,"explicit synthetic neutral");
        float[] input=[0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1];
        input[0]=-0f;var zero=HdrEnvironmentSource.Prepare(Guid.NewGuid(),4,2,input);input[0]=99;
        Pass(zero.Pixels[0]==0&&BitConverter.SingleToInt32Bits(zero.Pixels[0])==0,"source copy/canonical zero");
        Bad(()=>HdrEnvironmentSource.Prepare(Guid.Empty,4,2,input));Bad(()=>HdrEnvironmentSource.Prepare(Guid.NewGuid(),4,3,input));
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1f,65505f}){input[0]=value;Bad(()=>HdrEnvironmentSource.Prepare(Guid.NewGuid(),4,2,input));}input[0]=0;
        input[^1]=.5f;Bad(()=>HdrEnvironmentSource.Prepare(Guid.NewGuid(),4,2,input));input[^1]=1;
        EnvironmentLightingConfiguration.Off.Validate();Pass(true,"strict Off");
        new EnvironmentLightingConfiguration(Guid.NewGuid(),1,new string('A',64),1,.2f,true).Validate();
        Bad(()=>(EnvironmentLightingConfiguration.Off with{Strength=1}).Validate());
        Bad(()=>(EnvironmentLightingConfiguration.Off with{Enabled=true}).Validate());
        Bad(()=>new EnvironmentLightingConfiguration(Guid.NewGuid(),1,new string('a',64),1,0,true).Validate());
        EnvironmentPackage constant,directional;var source=Source(Guid.NewGuid(),new(4,2,.5f),Vector3.Zero);Guid asset=Guid.NewGuid();
        using(var loader=new PluginLoader()){
            loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());var module=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using(var cook=new EnvironmentCookService(module,()=>allowed)){
                Bad(()=>module.Dispose());Pass(module.OutstandingLeases==1,"module retained by cook lease");
                constant=cook.Cook(asset,1,source,settings);
                for(int f=0;f<6;f++){var row=constant.CopyIrradianceFace(f);for(int i=0;i<row.Length;i+=4)for(int c=0;c<3;c++)constantError=Math.Max(constantError,Math.Abs(row[i+c]-new Vector3(4,2,.5f)[c]*Math.PI));}
                Pass(constantError<.00001,"constant E=pi*L, linear HDR no gamma error="+constantError);
                for(int level=0;level<settings.Levels;level++)for(int f=0;f<6;f++){var row=constant.CopySpecularFace(level,f);Pass(row.Where((_,i)=>i%4!=3).Chunk(3).All(c=>c.SequenceEqual(new[]{4f,2f,.5f})),"constant specular mip "+level+"/"+f);}
                var lut=constant.CopyBrdfLut();int last=lut.Length-2;Pass(Math.Abs(lut[last]+lut[last+1]-(1-Math.Log(2)))<.005,"roughness1/NoV1 analytic BRDF energy");
                foreach(int y in new[]{6,10,15})foreach(int x in new[]{3,8,15}){
                    var expected=UniformBrdf(x/15d,y/15d);int at=(y*16+x)*2;lutError=Math.Max(lutError,Math.Max(Math.Abs(lut[at]-expected.X),Math.Abs(lut[at+1]-expected.Y)));
                }
                Pass(lutError<.02,"independent uniform hemisphere BRDF integral="+lutError);
                for(int y=3;y<16;y++)for(int x=3;x<16;x++){int at=(y*16+x)*2;energy=Math.Max(energy,lut[at]+lut[at+1]);}
                Pass(energy<=1.03,"measured single-scatter A+B grid energy="+energy);
                Pass(ReferenceEquals(constant,cook.Cook(asset,1,source,settings))&&cook.NativeCalls==1,"exact immutable cache reuse");
                allowed=false;Bad(()=>cook.Cook(asset,1,source,settings));allowed=true;
                Task.Run(()=>Bad(()=>cook.Cook(asset,1,source,settings))).GetAwaiter().GetResult();
                using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{cook.Cook(asset,1,source,settings,cancel.Token);throw new Exception("Cancellation accepted");}catch(OperationCanceledException){cases++;}}
                Bad(()=>cook.Cook(asset,0,source,settings));Bad(()=>cook.Cook(source.AssetId,1,source,settings));
                var ds=Source(Guid.NewGuid(),new(2,1,.5f),new(.75f,.3f,.2f));directional=cook.Cook(Guid.NewGuid(),2,ds,settings);
                for(int f=0;f<6;f++){var row=directional.CopyIrradianceFace(f);for(int y=0;y<8;y++)for(int x=0;x<8;x++){
                    var expected=(new Vector3(2,1,.5f)+new Vector3(.75f,.3f,.2f)*(2*Direction(f,x,y,8).X/3))*MathF.PI;int at=(y*8+x)*4;
                    for(int c=0;c<3;c++)directionError=Math.Max(directionError,Math.Abs(row[at+c]-expected[c]));
                }}
                Pass(directionError<.02,"directional E analytic "+directionError);
                for(int f=0;f<6;f++){var row=directional.CopySpecularFace(3,f);var expected=new Vector3(2,1,.5f)+new Vector3(.75f,.3f,.2f)*(2*Direction(f,0,0,1).X/3);
                    Pass(Enumerable.Range(0,3).All(c=>Math.Abs(row[c]-expected[c])<.02),"roughness1 direction convolution "+f);}
                var zeroPackage=cook.Cook(Guid.NewGuid(),1,zero,new(2,2,4,64));
                Pass(zeroPackage.CopyIrradianceFace(0)[0]==0&&zeroPackage.CopySpecularFace(0,0)[0]==0,"black environment zero energy");
                Pass(!constant.GpuValidated&&!directional.GpuValidated,"CPU package cannot assert GPU admission");
                // Actual pure UI renderer blocks offline 3D cooking, including a former cache key.
                using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"IBL pureUI exclusion",32,32,false);
                using(var ui=new RendererSession(module,window,32,32,pureUi:true)){
                    Bad(()=>cook.Cook(asset,1,source,settings));Pass(ui.UiStats.ResidentBytes==0&&ui.Stats.SubmittedFrames==0,"pure UI allocates no environment/frame");
                }
                using(var render=new RendererSession(module,window,32,32)){
                    var graph=new ClearPipeline(0,0,0).Build(32,32).Compile(render.Capabilities);
                    render.Submit(graph,null,RenderFrame.Reference(1,32,32));Bad(()=>cook.Cook(zeroPackage.AssetId,1,zero,new(2,2,4,64)));render.Present();
                    Pass(ReferenceEquals(zeroPackage,cook.Cook(zeroPackage.AssetId,1,zero,new(2,2,4,64))),"frame rejection retains exact cache");
                    Pass(render.Stats.ValidationErrors==0&&render.Stats.ValidationWarnings==0,"boundary fixture DX11 API0/0, not IBL drawing");
                }
                var hdr=Source(Guid.NewGuid(),new(65504,65504,65504),Vector3.Zero);
                var maximum=cook.Cook(Guid.NewGuid(),1,hdr,new(2,2,2,64));
                Pass(Math.Abs(maximum.CopyIrradianceFace(0)[0]-65504*MathF.PI)<.02&&maximum.CopySpecularFace(1,0)[0]==65504,"HDR maximum irradiance retains pi energy without half-float clipping");
                var defaults=cook.Cook(Guid.NewGuid(),1,neutral,EnvironmentCookSettings.Default);
                Pass(defaults.Settings==EnvironmentCookSettings.Default&&Math.Abs(defaults.CopyIrradianceFace(5)[0]-.18*Math.PI)<.000001,"real default preset cook");
            }
            EnvironmentCookService? reentrant=null;bool enter=false;
            using(var service=new EnvironmentCookService(module,()=>{if(enter)Bad(()=>reentrant!.Cook(asset,1,source,settings));return true;})){
                reentrant=service;enter=true;_ = service.Cook(asset,1,source,new(2,2,4,64));enter=false;
            }
            var disposed=new EnvironmentCookService(module,()=>true);disposed.Dispose();Bad(()=>disposed.Cook(asset,1,source,settings));
            int calls=0;using var revoked=new EnvironmentCookService(module,()=>++calls==1);Bad(()=>revoked.Cook(asset,1,source,new(2,2,4,64)));
            bool revokeAfter=false;int approvals=0;
            using(var transactional=new EnvironmentCookService(module,()=>!revokeAfter||++approvals==1)){
                var first=transactional.Cook(asset,1,source,new(2,2,4,64));revokeAfter=true;
                Bad(()=>transactional.Cook(asset,2,source,new(2,2,4,64)));revokeAfter=false;
                Pass(ReferenceEquals(first,transactional.Cook(asset,1,source,new(2,2,4,64)))&&transactional.NativeCalls==2,"post-cook revocation does not replace prior cache");
            }
            using(var cancelled=new CancellationTokenSource()){
                int cancelApprovals=0;
                using var cancelAfter=new EnvironmentCookService(module,()=>{if(++cancelApprovals==2)cancelled.Cancel();return true;});
                try{cancelAfter.Cook(asset,1,source,new(2,2,4,64),cancelled.Token);throw new Exception("Post-cook cancellation accepted");}catch(OperationCanceledException){cases++;}
                Pass(cancelAfter.NativeCalls==1,"post-cook cancellation checked before publication");
                var retry=cancelAfter.Cook(asset,1,source,new(2,2,4,64));Pass(cancelAfter.NativeCalls==2&&retry.Generation==1,"cancelled candidate was not cached");
            }
        }
        var original=constant.CopyBytes();
        Pass(EnvironmentPackage.Decode(original,constant.ContentHash).ContentHash==constant.ContentHash,"after plugin shutdown pure roundtrip");
        Pass(Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>EnvironmentPackage.Decode(constant.CopyBytes(),constant.ContentHash).CopyBrdfLut().SequenceEqual(constant.CopyBrdfLut())))).GetAwaiter().GetResult().All(x=>x),"immutable concurrent reads after plugin shutdown");
        byte[] plausible=(byte[])original.Clone();plausible[88]^=1;
        Pass(!EnvironmentPackage.Decode(plausible,Hash(plausible)).GpuValidated,"host-approved rehash is format integrity, not Cook provenance/GPU credential");
        byte[] copy=constant.CopyBytes();copy[^1]^=1;Pass(constant.CopyBytes().SequenceEqual(original),"package copy");
        var faceCopy=constant.CopyIrradianceFace(0);faceCopy[0]=0;Pass(constant.CopyIrradianceFace(0)[0]>10,"face copy");
        Bad(()=>constant.CopySpecularFace(99,0));Bad(()=>constant.CopyIrradianceFace(-1));Bad(()=>EnvironmentPackage.Decode(original,new string('0',64)));
        Bad(()=>EnvironmentPackage.Decode(original,constant.ContentHash.ToLowerInvariant()));
        foreach(int length in new[]{0,1,159,160,original.Length-1})Bad(()=>EnvironmentPackage.Decode(original.AsSpan(0,length),Hash(original.AsSpan(0,length).ToArray())));
        Bad(()=>EnvironmentPackage.Decode(new byte[EnvironmentPackage.MaxBytes+1],new string('A',64)));
        void Mutate(Action<byte[]> change,bool repair=false){byte[] bytes=(byte[])original.Clone();change(bytes);if(repair)SHA256.HashData(bytes.AsSpan(160)).CopyTo(bytes,120);Bad(()=>EnvironmentPackage.Decode(bytes,Hash(bytes)));}
        foreach(int field in new[]{0,4,8,12,16,20,24,28,32,36,40,44,152})Mutate(b=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(field),uint.MaxValue));
        Mutate(b=>Array.Clear(b,48,16));Mutate(b=>Array.Clear(b,64,16));Mutate(b=>Array.Copy(b,48,b,64,16));Mutate(b=>Array.Clear(b,80,8));Mutate(b=>Array.Clear(b,88,32));Mutate(b=>b[120]^=1);
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1f,-0f,1e9f})Mutate(b=>BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(160),value),true);
        Mutate(b=>BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(172),.5f),true);
        Mutate(b=>BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(b.Length-4),2.1f),true);
        var trailing=original.Concat(new byte[]{0}).ToArray();Bad(()=>EnvironmentPackage.Decode(trailing,Hash(trailing)));
        string path=Path.Combine(output,"constant.nce"),directionPath=Path.Combine(output,"directional.nce");File.WriteAllBytes(path,original);File.WriteAllBytes(directionPath,directional.CopyBytes());
        for(int i=0;i<2;i++){
            string childPath=i==0?path:directionPath,expected=i==0?constant.ContentHash:directional.ContentHash;
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{Assembly.GetExecutingAssembly().Location,"--environment-preflight",childPath,expected})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;string stderr=process.StandardError.ReadToEnd();process.WaitForExit();Pass(process.ExitCode==0,"independent no-native preflight "+stderr);
        }
        File.WriteAllText(Path.Combine(output,"environment-cook.json"),JsonSerializer.Serialize(new{schema=1,cases,constantError,directionError,lutError,energy,constantHash=constant.ContentHash,directionalHash=directional.ContentHash,gpuInstalled=false,manualHdrAcceptance=false}));
        Console.WriteLine($"PASS M7.3-A {cases} offline environment cases; constant={constantError},direction={directionError},BRDF={lutError},energy={energy}; GPU installation pending B");return 0;
    }
    // Independent uniform hemisphere quadrature, not GGX importance sampling or shared native math.
    private static Vector2 UniformBrdf(double nv,double rough)
    {
        const int nz=256,np=512;double sumA=0,sumB=0,k=rough*rough/2,alpha=rough*rough,a2=alpha*alpha;
        double vx=Math.Sqrt(1-nv*nv);
        for(int z=0;z<nz;z++){double nl=(z+.5)/nz,r=Math.Sqrt(1-nl*nl);
            for(int p=0;p<np;p++){double phi=2*Math.PI*(p+.5)/np,lx=r*Math.Cos(phi),ly=r*Math.Sin(phi),hx=vx+lx,hy=ly,hz=nv+nl;
                double length=Math.Sqrt(hx*hx+hy*hy+hz*hz);hx/=length;hz/=length;double vh=vx*hx+nv*hz,denom=hz*hz*(a2-1)+1;
                double d=a2/(Math.PI*denom*denom),g=nv/(nv*(1-k)+k)*nl/(nl*(1-k)+k),f=Math.Pow(1-vh,5),weight=d*g/(4*nv)*(2*Math.PI/(nz*np));
                sumA+=(1-f)*weight;sumB+=f*weight;
            }
        }
        return new((float)sumA,(float)sumB);
    }
}
