#include "../contracts/NcmaText.h"
#include "../PluginSupport.h"
#include <Windows.h>
#include <dwrite_3.h>
#include <d2d1.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <unordered_map>
#include <memory>
#include <vector>
#include <string>
#include <cmath>
#include <stdexcept>

using Microsoft::WRL::ComPtr;
namespace {
const auto owner=std::this_thread::get_id();uint64_t context=0,nextContext=1,nextResource=1,resident=0;bool comOwned=false;
ComPtr<IDWriteFactory5> factory;ComPtr<IDWriteInMemoryFontFileLoader> loader;ComPtr<ID2D1Factory> d2d;ComPtr<IWICImagingFactory> wic;
struct Font {ComPtr<IDWriteFontCollection1> collection;ComPtr<IDWriteFontFace> face;ComPtr<IDWriteFontFallback> fallback;std::wstring name;uint32_t bytes=0;uint64_t pins=0;};
struct Run {uint64_t font=0;std::vector<uint8_t> rgba;NcmaTextMetricsV1 metrics{};};
// Validate the SHAPED glyph runs, not each Unicode scalar independently: shaping can compose
// combining marks or ligatures even when an individual scalar has no standalone glyph.
class GlyphProbe final : public IDWriteTextRenderer {
    ULONG references=1;
public:
    bool missing=false;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** output) override {if(!output)return E_POINTER;if(id==__uuidof(IUnknown)||id==__uuidof(IDWriteTextRenderer)||id==__uuidof(IDWritePixelSnapping)){*output=static_cast<IDWriteTextRenderer*>(this);AddRef();return S_OK;}*output=nullptr;return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef() override{return ++references;}
    ULONG STDMETHODCALLTYPE Release() override{return --references;} // stack-owned for synchronous Layout::Draw only
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*,BOOL* disabled) override{*disabled=TRUE;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*,DWRITE_MATRIX* matrix) override{*matrix={1,0,0,1,0,0};return S_OK;}
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*,FLOAT* scale) override{*scale=1;return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*,FLOAT,FLOAT,DWRITE_MEASURING_MODE,const DWRITE_GLYPH_RUN* run,const DWRITE_GLYPH_RUN_DESCRIPTION*,IUnknown*) override {for(UINT32 i=0;i<run->glyphCount;i++)if(run->glyphIndices[i]==0&&run->glyphAdvances[i]>0)missing=true;return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*,FLOAT,FLOAT,const DWRITE_UNDERLINE*,IUnknown*) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*,FLOAT,FLOAT,const DWRITE_STRIKETHROUGH*,IUnknown*) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*,FLOAT,FLOAT,IDWriteInlineObject*,BOOL,BOOL,IUnknown*) override{return E_NOTIMPL;}
};
std::unordered_map<uint64_t,std::unique_ptr<Font>> fonts;std::unordered_map<uint64_t,std::unique_ptr<Run>> runs;
void Check(HRESULT hr){if(FAILED(hr))throw std::runtime_error("Text numerical API rejected font/layout/raster.");}
uint32_t Validate(uint64_t handle,NcmaErrorV1* error) {if(owner!=std::this_thread::get_id())return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);if(!context||handle!=context)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);return NCMA_OK;}
uint32_t NCMA_CALL Initialize(const uint8_t* input,uint32_t count,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{if(owner!=std::this_thread::get_id())return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);if(count||input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);if(context)return NcmaPlugin::Error(error,NCMA_BUSY);
        const HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(initialized)&&initialized!=RPC_E_CHANGED_MODE)Check(initialized);comOwned=SUCCEEDED(initialized);
        try{Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_ISOLATED,__uuidof(IDWriteFactory5),reinterpret_cast<IUnknown**>(factory.GetAddressOf())));
            Check(factory->CreateInMemoryFontFileLoader(&loader));Check(factory->RegisterFontFileLoader(loader.Get()));Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,d2d.GetAddressOf()));
            Check(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)));context=0x5458000000000000ull|nextContext++;*output=context;return NCMA_OK;
        }catch(...){if(factory&&loader)(void)factory->UnregisterFontFileLoader(loader.Get());wic.Reset();d2d.Reset();loader.Reset();factory.Reset();if(comOwned)CoUninitialize();comOwned=false;throw;}});
}
uint32_t NCMA_CALL Shutdown(uint64_t handle,NcmaErrorV1* error) noexcept {return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;if(!fonts.empty()||!runs.empty())return NcmaPlugin::Error(error,NCMA_BUSY);
    Check(factory->UnregisterFontFileLoader(loader.Get()));wic.Reset();d2d.Reset();loader.Reset();factory.Reset();if(comOwned)CoUninitialize();comOwned=false;context=0;return NCMA_OK;});}
uint32_t NCMA_CALL Status(uint64_t handle,NcmaModuleStatusV1* output,NcmaErrorV1* error) noexcept {return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);*output={sizeof(*output),1,fonts.size()+runs.size(),0,0};return NCMA_OK;});}
uint32_t NCMA_CALL CreateFont(uint64_t handle,const uint8_t* bytes,uint32_t count,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;
        if(!bytes||!output||count<12||count>32u*1024*1024||fonts.size()>=4||resident+count>64ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto font=std::make_unique<Font>();ComPtr<IDWriteFontFile> file;ComPtr<IDWriteFontSetBuilder1> builder;ComPtr<IDWriteFontSet> set;
        // null owner makes the system loader copy the bounded font bytes.
        Check(loader->CreateInMemoryFontFileReference(factory.Get(),bytes,count,nullptr,&file));Check(factory->CreateFontSetBuilder(&builder));Check(builder->AddFontFile(file.Get()));Check(builder->CreateFontSet(&set));Check(factory->CreateFontCollectionFromFontSet(set.Get(),&font->collection));
        if(!font->collection->GetFontFamilyCount())return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Empty font collection.");
        ComPtr<IDWriteFontFamily> family;ComPtr<IDWriteLocalizedStrings> names;ComPtr<IDWriteFont> face;
        Check(font->collection->GetFontFamily(0,&family));Check(family->GetFamilyNames(&names));UINT32 length=0;Check(names->GetStringLength(0,&length));if(length>1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        std::vector<wchar_t> name(length+1);Check(names->GetString(0,name.data(),length+1));font->name=name.data();Check(family->GetFirstMatchingFont(DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STRETCH_NORMAL,DWRITE_FONT_STYLE_NORMAL,&face));Check(face->CreateFontFace(&font->face));
        // Explicit fallback stays inside the supplied collection; no invisible OS/download fallback.
        ComPtr<IDWriteFontFallbackBuilder> fallback;Check(factory->CreateFontFallbackBuilder(&fallback));DWRITE_UNICODE_RANGE range{0,0x10ffff};const wchar_t* selected=font->name.c_str();
        Check(fallback->AddMapping(&range,1,&selected,1,font->collection.Get(),nullptr,nullptr,1));Check(fallback->CreateFontFallback(&font->fallback));
        font->bytes=count;uint64_t key=0x5446000000000000ull|nextResource++;fonts.emplace(key,std::move(font));resident+=count;*output=key;return NCMA_OK;});
}
uint32_t NCMA_CALL Prepare(uint64_t handle,const NcmaTextRequestV1* input,uint64_t* output,NcmaTextMetricsV1* metrics,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;
        if(!input||!output||!metrics||input->struct_size!=sizeof(*input)||!input->text||input->text_bytes>65536||!fonts.contains(input->font)||runs.size()>=128||input->wrap>1||input->right_to_left>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        const auto d=*input;if(!std::isfinite(d.size)||d.size<1||d.size>512||!std::isfinite(d.width)||d.width<=0||d.width>4096||!std::isfinite(d.height)||d.height<=0||d.height>4096||!std::isfinite(d.scale)||d.scale<.25f||d.scale>8||d.width*d.scale>4096||d.height*d.scale>4096)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto& font=*fonts.at(d.font);int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,reinterpret_cast<const char*>(d.text),static_cast<int>(d.text_bytes),nullptr,0);
        if(count<=0||count>16384)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Text Unicode/length.");std::wstring text(static_cast<size_t>(count),L'\0');
        if(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,reinterpret_cast<const char*>(d.text),static_cast<int>(d.text_bytes),text.data(),count)!=count)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        std::vector<UINT32> points;for(size_t i=0;i<text.size();i++){UINT32 p=text[i];if(p>=0xd800&&p<=0xdbff){if(i+1==text.size()||text[i+1]<0xdc00||text[i+1]>0xdfff)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);p=0x10000+((p-0xd800)<<10)+(text[++i]-0xdc00);}if(p!=9&&p!=10&&p!=13&&p!=0x200d&&p!=0x200c&&!(p>=0x202a&&p<=0x202e)&&!(p>=0x2066&&p<=0x2069))points.push_back(p);}
        ComPtr<IDWriteTextFormat> format;ComPtr<IDWriteTextLayout> layout;ComPtr<IDWriteTextLayout2> layout2;
        Check(factory->CreateTextFormat(font.name.c_str(),font.collection.Get(),DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,d.size,L"",&format));
        Check(format->SetWordWrapping(d.wrap?DWRITE_WORD_WRAPPING_WRAP:DWRITE_WORD_WRAPPING_NO_WRAP));Check(format->SetReadingDirection(d.right_to_left?DWRITE_READING_DIRECTION_RIGHT_TO_LEFT:DWRITE_READING_DIRECTION_LEFT_TO_RIGHT));
        Check(factory->CreateTextLayout(text.data(),static_cast<UINT32>(text.size()),format.Get(),d.width,d.height,&layout));Check(layout.As(&layout2));Check(layout2->SetFontFallback(font.fallback.Get()));DWRITE_TEXT_METRICS measure{};Check(layout->GetMetrics(&measure));
        GlyphProbe probe;Check(layout->Draw(nullptr,&probe,0,0));if(probe.missing)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Supplied font lacks required shaped glyphs; prepare an approved fallback font asset.");
        auto run=std::make_unique<Run>();run->font=d.font;auto& m=run->metrics;m={sizeof(m),static_cast<uint32_t>(std::ceil(d.width*d.scale)),static_cast<uint32_t>(std::ceil(d.height*d.scale)),0,measure.widthIncludingTrailingWhitespace,measure.height,measure.lineCount,0};
        const uint64_t bytes=static_cast<uint64_t>(m.width)*m.height*4;if(resident+bytes>128ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Text resident budget.");m.byte_count=static_cast<uint32_t>(bytes);
        ComPtr<IWICBitmap> bitmap;ComPtr<ID2D1RenderTarget> target;ComPtr<ID2D1SolidColorBrush> brush;
        Check(wic->CreateBitmap(m.width,m.height,GUID_WICPixelFormat32bppPBGRA,WICBitmapCacheOnLoad,&bitmap));
        const auto properties=D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96*d.scale,96*d.scale);
        Check(d2d->CreateWicBitmapRenderTarget(bitmap.Get(),properties,&target));Check(target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White),&brush));target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        target->BeginDraw();target->Clear(D2D1::ColorF(0,0,0,0));target->DrawTextLayout(D2D1::Point2F(0,0),layout.Get(),brush.Get(),D2D1_DRAW_TEXT_OPTIONS_CLIP);Check(target->EndDraw());
        run->rgba.resize(m.byte_count);Check(bitmap->CopyPixels(nullptr,m.width*4,m.byte_count,run->rgba.data()));
        for(size_t i=0;i<run->rgba.size();i+=4){run->rgba[i]=run->rgba[i+1]=run->rgba[i+2]=255;}
        uint64_t key=0x5452000000000000ull|nextResource++;*metrics=m;runs.emplace(key,std::move(run));font.pins++;resident+=bytes;*output=key;return NCMA_OK;});
}
uint32_t NCMA_CALL Copy(uint64_t handle,uint64_t key,uint8_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;if(!runs.contains(key))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);const auto& bytes=runs.at(key)->rgba;
    if(!output||capacity<bytes.size()){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=static_cast<uint32_t>(bytes.size());return NCMA_BUFFER_TOO_SMALL;}std::memcpy(output,bytes.data(),bytes.size());return NCMA_OK;});}
uint32_t NCMA_CALL Release(uint64_t handle,uint64_t key,NcmaErrorV1* error) noexcept {return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(handle,error);if(result)return result;
    if(auto run=runs.find(key);run!=runs.end()){fonts.at(run->second->font)->pins--;resident-=run->second->rgba.size();runs.erase(run);return NCMA_OK;}
    if(auto font=fonts.find(key);font!=fonts.end()){if(font->second->pins)return NcmaPlugin::Error(error,NCMA_BUSY);resident-=font->second->bytes;fonts.erase(font);return NCMA_OK;}return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);});}
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major,uint32_t minor,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    const NcmaTextApiV1 api{{sizeof(api),1,0,NCMA_TEXT_MODULE,7,Initialize,Shutdown,Status,NcmaPlugin::Diagnostic},CreateFont,Prepare,Copy,Release};return NcmaPlugin::CopyApi(major,minor,output,capacity,error,api);
}
