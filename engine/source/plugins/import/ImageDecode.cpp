#include "contracts/NcmaImage.h"
#include "PluginSupport.h"
#include <vector>
#if defined(_WIN32)
#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif
extern "C" NCMA_IMPORT_API uint32_t NCMA_CALL ncma_image_decode_v1(uint32_t version,const uint8_t* encoded,uint32_t bytes,NcmaImageInfoV1* info,uint8_t* rgba,uint32_t capacity,NcmaErrorV1* error) {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        if(version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
        if(!encoded||bytes<8||bytes>16u*1024*1024||!info||info->struct_size!=32||(!rgba&&capacity))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
#if defined(_WIN32)
        const uint8_t png[8]{137,80,78,71,13,10,26,10};const bool isPng=std::memcmp(encoded,png,8)==0,isJpeg=encoded[0]==255&&encoded[1]==216&&encoded[2]==255;
        if(!isPng&&!isJpeg)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Only PNG/JPEG memory decode.");
        struct Apartment {HRESULT result=CoInitializeEx(nullptr,COINIT_MULTITHREADED);~Apartment(){if(SUCCEEDED(result))CoUninitialize();}} apartment;
        if(FAILED(apartment.result))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Tool requires an MTA owner thread.");
        ComPtr<IWICImagingFactory> factory;ComPtr<IWICStream> stream;ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICFormatConverter> convert;
        auto failed=[&](){return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Malformed/unsupported image decode.");};
        if(FAILED(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory)))||
           FAILED(factory->CreateStream(&stream))||FAILED(stream->InitializeFromMemory(const_cast<BYTE*>(encoded),bytes))||
           FAILED(factory->CreateDecoderFromStream(stream.Get(),nullptr,WICDecodeMetadataCacheOnDemand,&decoder)))return failed();
        GUID container{};UINT count=0;
        if(FAILED(decoder->GetContainerFormat(&container))||((isPng&&container!=GUID_ContainerFormatPng)||(isJpeg&&container!=GUID_ContainerFormatJpeg))||
           FAILED(decoder->GetFrameCount(&count))||count!=1||FAILED(decoder->GetFrame(0,&frame)))return failed();
        UINT width=0,height=0,profiles=0;
        if(FAILED(frame->GetSize(&width,&height))||!width||!height||width>4096||height>4096||static_cast<uint64_t>(width)*height*4>64ull*1024*1024)return failed();
        HRESULT color=frame->GetColorContexts(0,nullptr,&profiles);
        if(SUCCEEDED(color)&&profiles)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Color profiles require explicit offline conversion.");
        ComPtr<IWICMetadataQueryReader> metadata;
        if(SUCCEEDED(frame->GetMetadataQueryReader(&metadata))) {
            PROPVARIANT orientation{};PropVariantInit(&orientation);
            if(SUCCEEDED(metadata->GetMetadataByName(L"/app1/ifd/{ushort=274}",&orientation))&&orientation.vt==VT_UI2&&orientation.uiVal!=1){PropVariantClear(&orientation);return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"EXIF orientation requires explicit offline conversion.");}
            PropVariantClear(&orientation);
        }
        const uint32_t required=width*height*4;*info={32,width,height,width*4,required,isPng?1u:2u,{0,0}};
        if(!rgba||capacity<required){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=required;return NCMA_BUFFER_TOO_SMALL;}
        if(FAILED(factory->CreateFormatConverter(&convert))||FAILED(convert->Initialize(frame.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom)))return failed();
        std::vector<uint8_t> pixels(required);
        if(FAILED(convert->CopyPixels(nullptr,width*4,required,pixels.data())))return failed();
        std::memcpy(rgba,pixels.data(),required);return NCMA_OK;
#else
        (void)rgba;(void)capacity;return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"WIC tool decode requires Windows.");
#endif
    });
}
