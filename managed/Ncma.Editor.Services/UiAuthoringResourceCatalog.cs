using System.Security.Cryptography;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Text;
using Ncma.Ui;
using Ncma.Ui.Rendering;

namespace Ncma.Editor.Services;

public sealed record UiResourceReview(Guid Id,string Source,UiResourceKind Kind,string Hash,string License,bool Redistributable);
// Trusted UI only. Exact project-relative file selection; copied prepared sources, no per-frame IO.
public sealed class UiAuthoringResourceCatalog(string root)
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid,FontAsset> _fonts=[];
    private readonly Dictionary<Guid,UiImageAsset> _images=[];
    private readonly Dictionary<Guid,UiResourceReview> _rows=[];
    private readonly Dictionary<Guid,long> _sizes=[];
    public ulong Revision { get; private set; }
    public IReadOnlyDictionary<Guid,FontAsset> Fonts{get{Verify();return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid,FontAsset>(_fonts);}}
    public IReadOnlyDictionary<Guid,UiImageAsset> Images{get{Verify();return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid,UiImageAsset>(_images);}}
    public UiResourceReview[] Rows { get { Verify();return _rows.Values.ToArray(); } }
    private void Verify(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("UI resource owner required.");}
    public bool Contains(Guid id,UiResourceKind kind) { Verify();return kind==UiResourceKind.Font?_fonts.ContainsKey(id):_images.ContainsKey(id); }
    public UiResourceReview AddFont(Guid id,string source,string expectedHash,string license,bool redistributable,TextService text)
    {
        Verify();byte[] bytes=UiAuthoringSource.Read(root,source,32*1024*1024);Check(id,bytes.Length,bytes,expectedHash);
        var font=new FontAsset(id,bytes,license,redistributable);using(var validation=text.CreateFont(font)){}var row=new UiResourceReview(id,source,UiResourceKind.Font,font.Hash,license,redistributable);
        _fonts[id]=font;Publish(row,bytes.Length);return row;
    }
    public UiResourceReview AddImage(Guid id,string source,string expectedHash,ImageDecoder decoder)
    {
        Verify();byte[] bytes=UiAuthoringSource.Read(root,source,16*1024*1024);Check(id,bytes.Length,bytes,expectedHash);
        var decoded=decoder.Decode(bytes,TextureSemantic.Color);long size=(long)decoded.Width*decoded.Height*4;Budget(id,size);
        var image=new UiImageAsset(id,decoded.Width,decoded.Height,decoded.Pixels[..checked((int)size)]);
        var row=new UiResourceReview(id,source,UiResourceKind.Image,expectedHash,"",false);_images[id]=image;Publish(row,size);return row;
    }
    private void Check(Guid id,long size,byte[] bytes,string hash)
    {
        if(id==Guid.Empty||_rows.ContainsKey(id)||Convert.ToHexString(SHA256.HashData(bytes))!=hash)throw new ArgumentException("UI exact resource identity/hash or duplicate.");Budget(id,size);
    }
    private void Budget(Guid id,long size) { if((_rows.Count>=64&&!_rows.ContainsKey(id))||_sizes.Values.Sum()+size>128L*1024*1024)throw new ArgumentException("UI resource catalog budget."); }
    private void Publish(UiResourceReview row,long size){_rows[row.Id]=row;_sizes[row.Id]=size;Revision=checked(Revision+1);}
    public void Require(UiDefinition definition)
    {
        Verify();foreach(var e in definition.Elements){if(e.Font!=Guid.Empty&&!_fonts.ContainsKey(e.Font))throw new ArgumentException("Missing explicitly approved Font UUID: "+e.Font);if(e.Image!=Guid.Empty&&!_images.ContainsKey(e.Image))throw new ArgumentException("Missing explicitly approved Image UUID: "+e.Image);}
    }
    public void Revoke() { Verify();_fonts.Clear();_images.Clear();_rows.Clear();_sizes.Clear();Revision=checked(Revision+1); }
}
