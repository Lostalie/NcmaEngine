// Real DX11 device and product adapter; only the selected backend HRESULT is injected.
#include "../../engine/source/plugins/contracts/NcmaRenderer.h"
#include "GuiFontAtlasTests.inl"
static void TestGuiDevicePreparation(const NcmaPlatformApiV1& platformApi, uint64_t platform,
    uint64_t window, uint64_t gm, NcmaWindowStateV1 state)
{
    NcmaErrorV1 error{};
    auto get = reinterpret_cast<NcmaGetApiV1>(GetProcAddress(GetModuleHandleW(L"NcmaRenderer.dll"), "ncma_plugin_get_api"));
    NcmaRendererApiV1 renderer{}; assert(get && get(1, 0, &renderer, sizeof(renderer), &error) == NCMA_OK);
    uint64_t rm = 0, device = 0;
    assert(renderer.module.initialize(nullptr, 0, &rm, &error) == NCMA_OK);
    NcmaRendererDescriptionV1 rd{sizeof(rd), 1, 1, 0, platform, window, 64, 64, {0, 0}};
    assert(renderer.create_renderer(rm, &rd, &device, &error) == NCMA_OK);
    NcmaGuiDescriptionV1 gd{sizeof(gd), 0, platform, window, nullptr, 0, 18, nullptr, 0, 0};
    uint64_t g = 0; assert(Create(gm, &gd, &g, &error) == NCMA_OK);
    assert(!ImGui_ImplDX11_Init(nullptr, nullptr));
    assert(!ImGui::GetIO().BackendRendererUserData);
    assert(AttachRenderer(gm, g, rm, device, &error) == NCMA_OK);
    assert(!gui->deviceObjectsReady && gui->preparationAttempts == 0);
    assert(renderer.destroy_renderer(rm, device, &error) == NCMA_BUSY);
    auto emptyObjects = [] {
        auto* b = ImGui_ImplDX11_GetBackendData(); assert(b);
        return !b->pFontSampler && !b->pFontTextureView && !b->pVertexShader && !b->pInputLayout &&
            !b->pVertexConstantBuffer && !b->pPixelShader && !b->pAtlasModeBuffer && !b->FontAlphaOnly && !b->pBlendState &&
            !b->pRasterizerState && !b->pDepthStencilState && !b->pVB && !b->pIB &&
            ImGui::GetIO().Fonts->TexID == 0;
    };
    assert(emptyObjects());
    auto finish = [&] {
        NcmaGuiFrameV1 frame{sizeof(frame), 0, 0, 0, gui->lastFrame + 1, 1, 1, 0};
        std::array<NcmaGuiEventV1, 256> events{};
        std::array<uint8_t, 65536> text{};
        uint32_t count = 0; NcmaGuiStatsV1 stats{};
        assert(Draw(gm, g, &frame, nullptr, nullptr, events.data(), 256, &count, text.data(), 65536, &stats, &error) == NCMA_OK);
    };
    uint64_t aggregate = 0;
    constexpr std::array activeSteps{2,3,4,15,6,7,8,9,10,11,12};
    for (size_t index = 0; index < activeSteps.size(); index++) {
        const int step = activeSteps[index];
        // Alternating DPI forces invalidation after an earlier successful frame.
        state.scale_x = state.scale_y = index % 2 ? 1.5f : 1.0f;
        std::fill(std::begin(preparationStepCalls), std::end(preparationStepCalls), 0u);
        preparationFailStep = step;
        NcmaGuiCaptureV1 capture{123, 45, 67, 89};
        assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_INTERNAL_ERROR);
        assert(capture.struct_size == 123 && capture.keyboard == 45 && capture.mouse == 67 && capture.cancel_interaction == 89);
        assert(!gui->frameActive && !gui->gpuDrawReady && !gui->deviceObjectsReady && emptyObjects());
        for (int i = 1; i <= 15; i++) {
            bool reached = false;
            for (size_t j = 0; j <= index; j++) reached = reached || activeSteps[j] == i;
            assert(preparationStepCalls[i] == (reached ? 1u : 0u));
        }
        assert(gui->preparationAttempts == index * 2 + 1 && gui->preparationFailures == index + 1);
        aggregate += gui->lastPreparationTicks;
        std::cout << "GUI_PREP failure step=" << step << " ticks=" << gui->lastPreparationTicks << '\n';
        // Same DPI after failure must retry, not mistake the raster scale for readiness.
        preparationFailStep = 0;
        assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK);
        assert(gui->deviceObjectsReady && gui->frameActive && !emptyObjects());
        aggregate += gui->lastPreparationTicks;
        assert(gui->preparationTicks == aggregate);
        std::cout << "GUI_PREP retry step=" << step << " ticks=" << gui->lastPreparationTicks << '\n';
        finish();
        const auto attempts = gui->preparationAttempts;
        preparationFailStep = 2; // A warm frame must never enter backend creation.
        assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK);
        assert(gui->preparationAttempts == attempts);
        finish();
    }
    preparationFailStep = 0;
    uint32_t required = 0;
    assert(Diagnostic(gm, nullptr, 0, &required, &error) == NCMA_BUFFER_TOO_SMALL && required > 0 && required < 2048);
    std::array<uint8_t, 2048> diagnostic{};
    diagnostic.fill(0xAB);
    assert(Diagnostic(gm, diagnostic.data(), required - 1, &required, &error) == NCMA_BUFFER_TOO_SMALL && diagnostic[0] == 0xAB);
    assert(Diagnostic(gm, diagnostic.data(), static_cast<uint32_t>(diagnostic.size()), &required, &error) == NCMA_OK);
    const std::string json(reinterpret_cast<char*>(diagnostic.data()), required);
    assert(json.find("\"attempts\":22,\"failures\":11") != std::string::npos);
    assert(json.find("\"performanceAccepted\":false") != std::string::npos);
    std::cout << json << '\n';
    assert(Destroy(gm, g, &error) == NCMA_OK);
    // New GUI generation on the same renderer has new resources and fresh counters;
    // destruction while failed also releases the renderer borrow.
    for (int i = 0; i < 3; i++) {
        uint64_t next = 0; assert(Create(gm, &gd, &next, &error) == NCMA_OK && next != g);
        g = next; assert(AttachRenderer(gm, g, rm, device, &error) == NCMA_OK);
        assert(gui->preparationAttempts == 0 && !gui->deviceObjectsReady);
        preparationFailStep = i == 2 ? 12 : 0;
        NcmaGuiCaptureV1 capture{};
        const uint32_t expected = i == 2 ? NCMA_INTERNAL_ERROR : NCMA_OK;
        assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == expected);
        if (i != 2) finish(); else assert(emptyObjects() && !gui->frameActive);
        assert(Destroy(gm, g, &error) == NCMA_OK);
    }
    preparationFailStep = 0;
    ID3D11Device* rawDevice=nullptr;ID3D11DeviceContext* rawContext=nullptr;
    assert(ncma_renderer_borrow_dx11_v1(rm,device,&rawDevice,&rawContext,&error)==NCMA_OK);
    TestGuiFontAtlasPixels(rawDevice,rawContext);
    assert(ncma_renderer_release_dx11_v1(rm,device,&error)==NCMA_OK);
    assert(renderer.destroy_renderer(rm, device, &error) == NCMA_OK);
    assert(renderer.module.shutdown(rm, &error) == NCMA_OK);
    NcmaModuleStatusV1 status{};
    assert(platformApi.module.get_status(platform, &status, &error) == NCMA_OK);
    assert(Diagnostic(gm, nullptr, 0, &required, &error) == NCMA_OK && required == 0);
    std::cout << "PASS GUI checked preparation: 11 device HRESULT failures/rollback/retry including mode buffer 15; retired steps 1/5/13/14 never invoked; warm no-create, DPI rebuild, 3 fresh GUI generations and failed destruction; not OS fault or performance acceptance\n";
}
