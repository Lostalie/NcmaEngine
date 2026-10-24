#include "GuiShaderReferenceTests.inl"
static void ClobberGuiTestPipeline(const ImDrawList*, const ImDrawCmd* command)
{
    auto* dc=ImGui_ImplDX11_GetBackendData()->pd3dDeviceContext;
    auto* buffer=static_cast<ID3D11Buffer*>(command->UserCallbackData);
    const UINT stride=4,offset=0;
    dc->IASetVertexBuffers(1,1,&buffer,&stride,&offset);
    dc->PSSetShader(nullptr,nullptr,0);
}
static void TestGuiFontAtlasPixels(ID3D11Device* device, ID3D11DeviceContext* dc)
{
    using Microsoft::WRL::ComPtr;
    constexpr UINT width=320,height=128;
    D3D11_TEXTURE2D_DESC targetDesc{};
    targetDesc.Width=width;targetDesc.Height=height;targetDesc.MipLevels=targetDesc.ArraySize=1;
    targetDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;targetDesc.SampleDesc.Count=1;
    targetDesc.Usage=D3D11_USAGE_DEFAULT;targetDesc.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> target,staging;
    ComPtr<ID3D11RenderTargetView> rtv;
    assert(SUCCEEDED(device->CreateTexture2D(&targetDesc,nullptr,&target)));
    assert(SUCCEEDED(device->CreateRenderTargetView(target.Get(),nullptr,&rtv)));
    targetDesc.Usage=D3D11_USAGE_STAGING;targetDesc.BindFlags=0;targetDesc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    assert(SUCCEEDED(device->CreateTexture2D(&targetDesc,nullptr,&staging)));
    D3D11_TEXTURE2D_DESC imageDesc=targetDesc;
    imageDesc.Width=imageDesc.Height=2;imageDesc.Usage=D3D11_USAGE_IMMUTABLE;
    imageDesc.BindFlags=D3D11_BIND_SHADER_RESOURCE;imageDesc.CPUAccessFlags=0;
    const std::array<uint8_t,16> rgba{255,0,0,255,0,255,0,160,0,0,255,80,255,255,0,0};
    D3D11_SUBRESOURCE_DATA imageData{rgba.data(),8,0};
    ComPtr<ID3D11Texture2D> image;ComPtr<ID3D11ShaderResourceView> imageView;
    assert(SUCCEEDED(device->CreateTexture2D(&imageDesc,&imageData,&image)));
    assert(SUCCEEDED(device->CreateShaderResourceView(image.Get(),nullptr,&imageView)));
    const UINT sentinelData[8]{7,9,11,13,15,17,19,21};
    D3D11_BUFFER_DESC sentinelDesc{};sentinelDesc.ByteWidth=sizeof(sentinelData);
    sentinelDesc.Usage=D3D11_USAGE_IMMUTABLE;sentinelDesc.BindFlags=D3D11_BIND_VERTEX_BUFFER;
    D3D11_SUBRESOURCE_DATA sentinelInput{sentinelData,0,0};
    ComPtr<ID3D11Buffer> sentinel;
    assert(SUCCEEDED(device->CreateBuffer(&sentinelDesc,&sentinelInput,&sentinel)));
    unsigned comparisons=0;
    for(bool cjk:{false,true})for(float scale:{1.0f,1.5f,2.0f}) {
        auto* context=ImGui::CreateContext();auto& io=ImGui::GetIO();
        io.IniFilename=nullptr;io.LogFilename=nullptr;io.DisplaySize={static_cast<float>(width),static_cast<float>(height)};io.DeltaTime=1.0f/60;
        if(cjk)assert(io.Fonts->AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc",18*scale,nullptr,io.Fonts->GetGlyphRangesChineseSimplifiedCommon()));
        else {ImFontConfig config;config.SizePixels=18*scale;io.Fonts->AddFontDefault(&config);}
        unsigned char* alpha=nullptr;int aw=0,ah=0;io.Fonts->GetTexDataAsAlpha8(&alpha,&aw,&ah);
        assert(alpha&&aw>0&&ah>0&&io.Fonts->TexPixelsRGBA32==nullptr);
        io.FontGlobalScale=1/scale;
        assert(ImGui_ImplDX11_Init(device,dc));
        auto render=[&](bool forceRgba,bool colored,bool runtimeReference=false) {
            GuiShaderReferenceScope referenceScope;
            io.Fonts->TexPixelsUseColors=forceRgba;
            if(colored) {
                unsigned char* pixels=nullptr;io.Fonts->GetTexDataAsRGBA32(&pixels,nullptr,nullptr);
                for(int i=0;i<aw*ah;i++){pixels[i*4]=137;pixels[i*4+1]=83;pixels[i*4+2]=211;}
                // A legitimate RGBA-only atlas must not be rebuilt through GetAlpha8.
                IM_FREE(io.Fonts->TexPixelsAlpha8);io.Fonts->TexPixelsAlpha8=nullptr;
            }
            assert(ImGui_ImplDX11_CreateDeviceObjects());
            if(runtimeReference)UseRuntimeGuiShaderReference(device,referenceScope);
            auto* bd=ImGui_ImplDX11_GetBackendData();
            assert(bd->FontAlphaOnly==!forceRgba && bd->pAtlasModeBuffer);
            D3D11_BUFFER_DESC modeDescription{};bd->pAtlasModeBuffer->GetDesc(&modeDescription);
            assert(modeDescription.ByteWidth==8 && modeDescription.Usage==D3D11_USAGE_IMMUTABLE &&
                modeDescription.BindFlags==D3D11_BIND_VERTEX_BUFFER && modeDescription.CPUAccessFlags==0);
            D3D11_SHADER_RESOURCE_VIEW_DESC actualView{};bd->pFontTextureView->GetDesc(&actualView);
            assert(actualView.Format==(forceRgba?DXGI_FORMAT_R8G8B8A8_UNORM:DXGI_FORMAT_R8_UNORM));
            if(!forceRgba)assert(io.Fonts->TexPixelsRGBA32==nullptr);
            ImGui::NewFrame();
            auto* draw=ImGui::GetBackgroundDrawList();
            // >64K vertices force nonzero VtxOffset in the 16-bit index build.
            // They are clipped offscreen, so the visible oracle remains compact.
            for(int i=0;i<17000;i++)draw->AddRectFilled({-100,-100},{-99,-99},IM_COL32_WHITE);
            draw->AddText({8,8},IM_COL32(241,190,127,220),cjk?reinterpret_cast<const char*>(u8"字体测试 ABC 0123"):"Font ABC 0123");
            draw->AddRectFilled({8,76},{28,96},IM_COL32_WHITE);
            draw->AddImage(reinterpret_cast<ImTextureID>(imageView.Get()),{210,8},{290,64});
            draw->AddCallback(ClobberGuiTestPipeline,sentinel.Get());
            draw->AddCallback(ImDrawCallback_ResetRenderState,nullptr);
            draw->AddText({8,40},IM_COL32(80,240,150,255),"after image/reset");
            draw->AddImage(reinterpret_cast<ImTextureID>(imageView.Get()),{210,76},{260,116});
            ImGui::GetForegroundDrawList()->AddRectFilled({300,8},{308,16},IM_COL32(250,10,40,255));
            ImGui::Render();
            const auto* drawData=ImGui::GetDrawData();
            assert(drawData->TotalVtxCount>65536 && drawData->CmdListsCount>=2);
            bool offsetDraw=false;
            for(const auto* list:drawData->CmdLists)for(const auto& command:list->CmdBuffer)
                offsetDraw=offsetDraw||(!command.UserCallback&&command.ElemCount&&command.VtxOffset>0&&command.IdxOffset>0);
            assert(offsetDraw);
            ID3D11RenderTargetView* view=rtv.Get();dc->OMSetRenderTargets(1,&view,nullptr);
            const float clear[]{0.1f,0.2f,0.3f,0.4f};dc->ClearRenderTargetView(view,clear);
            dc->PSSetShader(bd->pPixelShader,nullptr,0);
            ID3D11Buffer* incoming[2]{sentinel.Get(),forceRgba?nullptr:sentinel.Get()};
            const UINT strides[2]{20,forceRgba?0u:4u},offsets[2]{8,forceRgba?0u:4u};
            dc->IASetVertexBuffers(0,2,incoming,strides,offsets);
            ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
            ID3D11Buffer* restoredBuffers[2]{};UINT restoredStrides[2]{},restoredOffsets[2]{};
            dc->IAGetVertexBuffers(0,2,restoredBuffers,restoredStrides,restoredOffsets);
            for(int slot=0;slot<2;slot++) {
                assert(restoredBuffers[slot]==incoming[slot]&&restoredStrides[slot]==strides[slot]&&restoredOffsets[slot]==offsets[slot]);
                if(restoredBuffers[slot])restoredBuffers[slot]->Release();
            }
            ComPtr<ID3D11PixelShader> restored;dc->PSGetShader(&restored,nullptr,nullptr);
            assert(restored.Get()==bd->pPixelShader);
            dc->CopyResource(staging.Get(),target.Get());
            D3D11_QUERY_DESC qd{D3D11_QUERY_EVENT,0};ComPtr<ID3D11Query> done;
            assert(SUCCEEDED(device->CreateQuery(&qd,&done)));dc->End(done.Get());dc->Flush();
            const ULONGLONG start=GetTickCount64();
            for(;;) {
                const HRESULT hr=dc->GetData(done.Get(),nullptr,0,D3D11_ASYNC_GETDATA_DONOTFLUSH);
                if(hr==S_OK)break;
                assert(hr==S_FALSE && GetTickCount64()-start<2000);Sleep(1);
            }
            D3D11_MAPPED_SUBRESOURCE mapped{};assert(SUCCEEDED(dc->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped)));
            std::vector<uint8_t> result(width*height*4);
            for(UINT y=0;y<height;y++)std::memcpy(result.data()+y*width*4,static_cast<uint8_t*>(mapped.pData)+y*mapped.RowPitch,width*4);
            dc->Unmap(staging.Get(),0);
            if(colored) {
                const size_t offset=(85*width+15)*4;
                assert(result[offset]==137&&result[offset+1]==83&&result[offset+2]==211&&result[offset+3]==255);
            }
            return result;
        };
        const auto compact=render(false,false);
        const auto oldCompact=render(false,false,true);
        assert(compact==oldCompact);
        const auto reference=render(true,false);
        const auto oldReference=render(true,false,true);
        assert(reference==oldReference);
        assert(compact==reference);comparisons++;
        const auto colored=render(true,true); // Actual RGB atlas content must not be reduced to alpha.
        const auto oldColored=render(true,true,true);
        assert(colored==oldColored);
        ImGui_ImplDX11_Shutdown();ImGui::DestroyContext(context);
    }
    dc->ClearState();dc->Flush();
    std::cout<<"PASS GUI R8 atlas: "<<comparisons<<" exact GPU RGBA comparisons, Latin/CJK 100/150/200%, mixed images/reset, RGBA-only colored fallback, shader state restore; 75% smaller font upload payload, not residency/FPS acceptance\n";
    std::cout<<"PASS GUI unified shaders: 18 exact GPU comparisons against independent old VS/image/font and DrawIndexed reference; immutable 8-byte mode, both VB slots/null-strides-offsets restored, custom callback/reset, >64K vertices/nonzero VtxOffset/IdxOffset and multiple lists; no runtime compilation in production GUI\n";
}
