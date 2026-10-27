// Real ImGui input; fixture token only, never printed or placed in ordinary event packets.
void TestGuiSecretInput(uint64_t gm,uint64_t g,NcmaWindowStateV1 state,uint64_t nextFrame) {
    NcmaErrorV1 error{};NcmaGuiCaptureV1 capture{};NcmaGuiStatsV1 stats{};
    std::array<NcmaGuiEventV1,256> events{};std::array<uint8_t,65536> output{};uint32_t count=0;
    const std::string text="Secret panel##key";
    std::array<NcmaGuiItemV1,3> items{};
    items[0].kind=NCMA_GUI_PANEL_BEGIN;items[0].enabled=1;items[0].widget_high=81;items[0].widget_low=1;
    items[0].label_length=12;items[0].rect[2]=500;items[0].rect[3]=300;
    items[1].kind=NCMA_GUI_SECRET_TEXT;items[1].enabled=1;items[1].widget_high=81;items[1].widget_low=30;
    items[1].label_offset=12;items[1].label_length=5;items[1].value=1;
    items[2].kind=NCMA_GUI_PANEL_END;
    NcmaGuiFrameV1 frame{sizeof(frame),3,static_cast<uint32_t>(text.size()),0,nextFrame,90,90,0};
    state.scale_x=state.scale_y=1;state.framebuffer_width=state.width;state.framebuffer_height=state.height;
    auto draw=[&](){++frame.frame;assert(Begin(gm,g,&state,1.0/60,&capture,&error)==NCMA_OK);
        output.fill(0x5a);assert(Draw(gm,g,&frame,items.data(),reinterpret_cast<const uint8_t*>(text.data()),events.data(),256,&count,output.data(),65536,&stats,&error)==NCMA_OK);
        assert(count==0&&stats.text_bytes==0&&std::all_of(output.begin(),output.end(),[](uint8_t v){return v==0x5a;}));};
    draw();auto& io=ImGui::GetIO();
    io.AddMousePosEvent((recordedButtonMin.x+recordedButtonMax.x)/2,(recordedButtonMin.y+recordedButtonMax.y)/2);draw();
    io.AddMouseButtonEvent(0,true);draw();io.AddMouseButtonEvent(0,false);draw();
    io.AddInputCharactersUTF8("fake-input-token");draw();
    assert(std::string(gui->secret.data())=="fake-input-token");
    assert((gui->context->InputTextState.Flags&ImGuiInputTextFlags_Password)!=0);
    assert((gui->context->InputTextState.Flags&ImGuiInputTextFlags_NoUndoRedo)!=0);
    assert(!gui->textDrafts.contains(Id{81,30})&&!gui->textPending.contains(Id{81,30}));
    std::array<uint8_t,4097> taken{};uint32_t bytes=0;auto stale=frame;stale.frame--;
    assert(TakeSecret(gm,g,&stale,81,30,taken.data(),4097,&bytes,&error)==NCMA_INVALID_ARGUMENT);
    assert(TakeSecret(gm,g,&frame,81,30,taken.data(),4097,&bytes,&error)==NCMA_OK&&bytes==16);
    assert(std::memcmp(taken.data(),"fake-input-token",16)==0&&gui->secretEpoch==0);
    assert(std::all_of(gui->secret.begin(),gui->secret.end(),[](char v){return v==0;}));
    assert(TakeSecret(gm,g,&frame,81,30,taken.data(),4097,&bytes,&error)==NCMA_INVALID_ARGUMENT);
    SecureZeroMemory(taken.data(),taken.size());draw();
    // Bounded buffer and each invalidation path; no production injection API.
    gui->secret.fill('x');gui->secret.back()=0;
    assert(TakeSecret(gm,g,&frame,81,30,taken.data(),4097,&bytes,&error)==NCMA_OK&&bytes==4096);
    SecureZeroMemory(taken.data(),taken.size());draw();gui->secret[0]='x';items[1].value=2;draw();assert(gui->secret[0]==0);
    gui->secret[0]='x';state.focused=0;draw();assert(gui->secret[0]==0);state.focused=1;
    draw();gui->secret[0]='x';state.input_reset=1;draw();assert(gui->secret[0]==0);state.input_reset=0;
    draw();gui->secret[0]='x';items[1].enabled=0;draw();assert(gui->secretEpoch==0&&gui->secret[0]==0);
    items[1].enabled=1;draw();gui->secret[0]='x';assert(ClearSecret(gm,g,&error)==NCMA_OK&&gui->secretEpoch==0);
    draw();gui->secret[0]='x';items[1].kind=NCMA_GUI_LABEL;draw();assert(gui->secretEpoch==0&&gui->secret[0]==0);
    std::cout<<"PASS GUI1.8 secret: actual masked typing, no ordinary events/text, exact-frame one-shot take, 4096 bound, epoch/focus/reset/disable/remove/clear\n";
}
