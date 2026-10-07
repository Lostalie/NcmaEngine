using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ncma.Assets;
namespace Ncma.Animation.Native;

[StructLayout(LayoutKind.Sequential)] public readonly record struct PoseTrs(Vector3 Position,Quaternion Rotation,Vector3 Scale);
[StructLayout(LayoutKind.Sequential)] public struct PoseStatistics { public uint Size,MaxRequests; public ulong Rigs,Clips,RetainedBytes,SampleCalls,SampledBones; }
public readonly record struct PoseSample(PoseRig Rig,PoseClip? Clip,ClipSampleTimes Times);
public sealed class PoseRig : IDisposable
{
    internal readonly PoseKernel Owner;internal ulong Key;public int BoneCount{get;}
    internal PoseRig(PoseKernel owner,ulong key,int bones){Owner=owner;Key=key;BoneCount=bones;}
    public void Dispose(){if(Key==0)return;Owner.Release(Key);Key=0;}
}
public sealed class PoseClip : IDisposable
{
    internal readonly PoseKernel Owner;internal ulong Key;public PoseRig Rig{get;}public double Duration{get;}
    internal PoseClip(PoseKernel owner,ulong key,PoseRig rig,double duration){Owner=owner;Key=key;Rig=rig;Duration=duration;}
    public void Dispose(){if(Key==0)return;Owner.Release(Key);Key=0;}
}
// Trusted bootstrap explicitly pins/verifies an absolute kernel path; not an Agent loading endpoint.
public sealed unsafe partial class PoseKernel : IDisposable
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly HashSet<ulong> _resources=[];
    private KernelCodePin? _pin;private nint _library;private ulong _context;private Api _api;
    public PoseKernel(string libraryPath,string expectedHash,bool blendSupport=false)
    {
        AssetRecordCodec.ValidateHash(expectedHash);
        if(!Path.IsPathFullyQualified(libraryPath) || Path.GetFileName(libraryPath)!="NcmaAnimationKernel.dll")throw new ArgumentException("Explicit animation kernel filename/path required.");
        for(var part=new FileInfo(libraryPath) as FileSystemInfo;part is not null;part=part is FileInfo f?f.Directory:((DirectoryInfo)part).Parent)
            if((part.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Animation kernel path traverses a reparse point.");
        if(IntPtr.Size!=8 || sizeof(Api)!=72 || sizeof(Error)!=528 || sizeof(PoseTrs)!=40 || sizeof(Key)!=48 || sizeof(Request)!=40 || sizeof(Matrix4x4)!=64)
            throw new PlatformNotSupportedException("Validated x64 pose layouts required.");
        _pin=new(libraryPath);
        try{
            if(Convert.ToHexString(SHA256.HashData(_pin.Stream))!=expectedHash)throw new ArgumentException("Animation kernel hash mismatch.");
            _library=NativeLibrary.Load(libraryPath,typeof(PoseKernel).Assembly,DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32);
            var get=(delegate* unmanaged[Cdecl]<uint,uint,void*,uint,Error*,uint>)NativeLibrary.GetExport(_library,"ncma_pose_get_api");
            Api api=default;Error error=default;Check(get(1,0,&api,72,&error),error);
            if(api.Size!=72 || api.Major!=1 || api.Minor!=0 || api.MaxBones!=1024 || api.Create==null || api.Close==null || api.Rig==null || api.Clip==null || api.Release==null || api.Sample==null || api.Stats==null)throw new ArgumentException("Pose API table rejected.");
            if(blendSupport)LoadBlendExtension(); // Validate optional table before creating any context/resource.
            ulong context=0;Check(api.Create(&context,&error),error);if(context==0)throw new ArgumentException("Null pose context.");_api=api;_context=context;
        }catch{if(_library!=0)NativeLibrary.Free(_library);_library=0;_pin.Dispose();_pin=null;throw;}
    }
    public PoseRig CreateRig(SkeletonPayload skeleton)
    {
        Verify();var bones=ModelPayloadCodec.DecodeSkeleton(ModelPayloadCodec.Encode(skeleton)).Bones;
        var data=new Bone[bones.Length];for(int i=0;i<data.Length;i++)data[i]=new(){Parent=bones[i].Parent,Local=Trs(bones[i].BindLocal)};
        var resource=new PoseRig(this,0,bones.Length);_resources.EnsureCapacity(_resources.Count+1);
        ulong key=0;Error error=default;fixed(Bone* p=data)Check(_api.Rig(_context,p,(uint)data.Length,&key,&error),error);
        _resources.Add(key);resource.Key=key;return resource;
    }
    public PoseClip CreateClip(PoseRig rig,ClipPayload clip)
    {
        Verify();CheckRig(rig);var value=ModelPayloadCodec.DecodeClip(ModelPayloadCodec.Encode(clip));
        if(value.BoneCount!=rig.BoneCount)throw new ArgumentException("Clip/skeleton bone count mismatch.");
        var tracks=new Track[value.Clip.Tracks.Length];int count=checked(value.Clip.Tracks.Sum(t=>t.Keys.Length));var keys=new Key[count];int offset=0;
        for(int t=0;t<tracks.Length;t++){var source=value.Clip.Tracks[t];tracks[t]=new(){Bone=source.Bone,First=(uint)offset,Count=(uint)source.Keys.Length};foreach(var k in source.Keys)keys[offset++]=new(){Time=k.Time,Local=Trs(k.Value)};}
        var resource=new PoseClip(this,0,rig,value.Clip.Duration);_resources.EnsureCapacity(_resources.Count+1);
        ulong key=0;Error error=default;fixed(Track* t=tracks)fixed(Key* k=keys)Check(_api.Clip(_context,rig.Key,value.Clip.Duration,t,(uint)tracks.Length,k,(uint)keys.Length,&key,&error),error);
        _resources.Add(key);resource.Key=key;return resource;
    }
    // Reusable caller spans; one bounded batch, local-TRS interpolation occurs in native before model composition.
    public int Sample(ReadOnlySpan<PoseSample> samples,Span<PoseTrs> local,Span<Matrix4x4> model)
    {
        Verify();if(samples.Length is <1 or >32)throw new ArgumentException("Pose batch supports 1..32 requests.");
        Span<Request> requests=stackalloc Request[32];int offset=0;
        for(int i=0;i<samples.Length;i++){var s=samples[i];CheckRig(s.Rig);
            if(s.Clip is not null && (s.Clip.Owner!=this || s.Clip.Key==0 || s.Clip.Rig!=s.Rig))throw new ArgumentException("Foreign/stale pose clip.");
            requests[i]=new(){Rig=s.Rig.Key,Clip=s.Clip?.Key??0,Previous=s.Times.Previous,Current=s.Times.Current,Alpha=s.Times.Alpha,Offset=(uint)offset};offset=checked(offset+s.Rig.BoneCount);}
        if(offset>32768 || local.Length<offset || model.Length<offset)throw new ArgumentException("Pose output capacity.");
        if(MemoryMarshal.AsBytes(local).Overlaps(MemoryMarshal.AsBytes(model)))throw new ArgumentException("Pose outputs cannot alias.");
        Error error=default;fixed(Request* r=requests)fixed(PoseTrs* l=local)fixed(Matrix4x4* m=model)Check(_api.Sample(_context,r,(uint)samples.Length,l,m,(uint)offset,&error),error);return offset;
    }
    public PoseStatistics Statistics {get{Verify();PoseStatistics s=default;Error error=default;Check(_api.Stats(_context,&s,&error),error);if(s.Size!=48 || s.MaxRequests!=32)throw new ArgumentException("Pose stats contract.");return s;}}
    private static PoseTrs Trs(ImportTransform t)=>new(t.Position,t.Rotation,t.Scale);
    private void CheckRig(PoseRig rig){if(rig is null || rig.Owner!=this || rig.Key==0)throw new ArgumentException("Foreign/stale pose rig.");}
    internal void Release(ulong key){Verify();Error error=default;Check(_api.Release(_context,key,&error),error);_resources.Remove(key);}
    private void Verify(){if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Pose kernel requires owner thread.");ObjectDisposedException.ThrowIf(_context==0,this);}
    public void Dispose(){if(_context==0)return;Verify();if(_resources.Count!=0)throw new InvalidOperationException("Release clips/rigs before closing pose kernel.");Error error=default;Check(_api.Close(_context,&error),error);_context=0;NativeLibrary.Free(_library);_library=0;_pin!.Dispose();_pin=null;}
    private static void Check(uint result,Error error){if(result!=0)throw new InvalidOperationException($"Pose kernel rejected operation: {result} (reported {error.Code}).");}
    [StructLayout(LayoutKind.Sequential)] private struct Bone{public int Parent;public PoseTrs Local;}
    [StructLayout(LayoutKind.Sequential)] private struct Key{public double Time;public PoseTrs Local;}
    [StructLayout(LayoutKind.Sequential)] private struct Track{public uint Bone,First,Count,Reserved;}
    [StructLayout(LayoutKind.Sequential)] private struct Request{public ulong Rig,Clip;public double Previous,Current;public float Alpha;public uint Offset;}
    [StructLayout(LayoutKind.Sequential)] private struct Error{public uint Code,Reserved,Required,Length;public fixed byte Message[512];}
    [StructLayout(LayoutKind.Sequential)] private struct Api{
        public uint Size,Major,Minor,MaxBones;
        public delegate* unmanaged[Cdecl]<ulong*,Error*,uint> Create;
        public delegate* unmanaged[Cdecl]<ulong,Error*,uint> Close;
        public delegate* unmanaged[Cdecl]<ulong,Bone*,uint,ulong*,Error*,uint> Rig;
        public delegate* unmanaged[Cdecl]<ulong,ulong,double,Track*,uint,Key*,uint,ulong*,Error*,uint> Clip;
        public delegate* unmanaged[Cdecl]<ulong,ulong,Error*,uint> Release;
        public delegate* unmanaged[Cdecl]<ulong,Request*,uint,PoseTrs*,Matrix4x4*,uint,Error*,uint> Sample;
        public delegate* unmanaged[Cdecl]<ulong,PoseStatistics*,Error*,uint> Stats;
    }
}
