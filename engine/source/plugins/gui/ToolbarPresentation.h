#pragma once
#include <imgui.h>
#include <algorithm>
#include <cmath>

// Copied, scoped visual primitives only. No engine commands, selection or editor state.
namespace NcmaToolbar {
inline void Icon(ImDrawList*,ImVec2,float,unsigned,ImU32);
inline void WorkspaceTheme() {
    ImGui::StyleColorsDark();auto& s=ImGui::GetStyle();
    s.WindowRounding=4;s.FrameRounding=4;s.PopupRounding=4;s.TabRounding=4;
    s.WindowPadding={8,8};s.FramePadding={8,5};s.ItemSpacing={8,8};s.WindowBorderSize=1;
    auto color=[](int r,int g,int b){return ImVec4(r/255.f,g/255.f,b/255.f,1);};
    auto* c=s.Colors;
    c[ImGuiCol_WindowBg]=color(20,33,49);c[ImGuiCol_ChildBg]=c[ImGuiCol_WindowBg];
    c[ImGuiCol_PopupBg]=color(16,27,41);c[ImGuiCol_Border]=color(43,67,90);
    c[ImGuiCol_TitleBg]=color(16,27,41);c[ImGuiCol_TitleBgActive]=color(24,41,59);
    c[ImGuiCol_Text]=color(223,233,243);c[ImGuiCol_TextDisabled]=color(147,167,189);
    c[ImGuiCol_FrameBg]=color(16,27,41);c[ImGuiCol_FrameBgHovered]=color(32,54,77);c[ImGuiCol_FrameBgActive]=color(38,66,95);
    c[ImGuiCol_Button]=color(29,49,70);c[ImGuiCol_ButtonHovered]=color(39,71,105);c[ImGuiCol_ButtonActive]=color(37,102,168);
    c[ImGuiCol_Header]=c[ImGuiCol_Button];c[ImGuiCol_HeaderHovered]=c[ImGuiCol_ButtonHovered];c[ImGuiCol_HeaderActive]=c[ImGuiCol_ButtonActive];
    c[ImGuiCol_CheckMark]=color(37,140,255);c[ImGuiCol_SliderGrab]=c[ImGuiCol_CheckMark];c[ImGuiCol_Separator]=c[ImGuiCol_Border];
}
inline bool MenuButton(const char* id,const std::string& caption,const std::string& tooltip,const NcmaGuiItemV1& item) {
    ImGui::SetCursorScreenPos({item.rect[0],item.rect[1]});
    bool pressed=ImGui::InvisibleButton(id,{item.rect[2],item.rect[3]});auto* d=ImGui::GetWindowDrawList();
    bool hover=ImGui::IsItemHovered(ImGuiHoveredFlags_AllowWhenDisabled),active=ImGui::IsItemActive();
    const ImVec2 a{item.rect[0],item.rect[1]},b{a.x+item.rect[2],a.y+item.rect[3]};
    bool accent=item.minimum==1,selected=item.minimum==2;
    if(accent||selected||hover||active)d->AddRectFilled(a,b,accent?IM_COL32(28,101,82,255):IM_COL32(29,49,70,255),4);
    ImU32 color=item.enabled?IM_COL32(223,233,243,255):IM_COL32(106,127,150,255);
    float size=16,textWidth=ImGui::GetFont()->CalcTextSizeA(size,FLT_MAX,0,caption.c_str()).x;
    float iconWidth=item.value?23.f:0.f;float x=a.x+std::max(4.f,(item.rect[2]-textWidth-iconWidth)*.5f);
    d->PushClipRect(a,b,true);
    if(item.value){Icon(d,{x+8,a.y+item.rect[3]*.5f},16,static_cast<unsigned>(item.value),color);x+=iconWidth;}
    d->AddText(ImGui::GetFont(),size,{x,a.y+(item.rect[3]-size)*.5f},color,caption.c_str());d->PopClipRect();
    if(selected)d->AddLine({a.x+4,b.y-1},{b.x-4,b.y-1},IM_COL32(37,140,255,255),2);
    if(hover&&!tooltip.empty())ImGui::SetTooltip("%s",tooltip.c_str());return pressed&&item.enabled;
}
inline void Icon(ImDrawList* d,ImVec2 c,float size,unsigned icon,ImU32 color) {
    const float s=size*.5f,t=1.7f;
    auto p=[&](float x,float y){return ImVec2(c.x+x*s,c.y+y*s);};
    auto line=[&](float ax,float ay,float bx,float by){d->AddLine(p(ax,ay),p(bx,by),color,t);};
    switch(icon) {
    case 1:d->AddRect(p(-.85f,-.5f),p(.85f,.65f),color,2,0,t);line(-.85f,-.5f,-.85f,-.8f);line(-.85f,-.8f,-.2f,-.8f);line(-.2f,-.8f,.05f,-.5f);break;
    case 2:line(0,-.9f,.8f,-.45f);line(.8f,-.45f,.8f,.5f);line(.8f,.5f,0,.95f);line(0,.95f,-.8f,.5f);line(-.8f,.5f,-.8f,-.45f);line(-.8f,-.45f,0,-.9f);line(-.8f,-.45f,0,0);line(0,0,.8f,-.45f);line(0,0,0,.95f);break;
    case 3:d->AddRect(p(-.8f,-.8f),p(.8f,.8f),color,2,0,t);d->AddRect(p(-.4f,-.8f),p(.4f,-.2f),color,0,0,t);d->AddRect(p(-.45f,.2f),p(.45f,.8f),color,0,0,t);break;
    case 4:d->AddCircle(c,size*.3f,color,16,t);for(int i=0;i<8;i++){float a=static_cast<float>(i)*.785398f;line(std::cos(a)*.65f,std::sin(a)*.65f,std::cos(a)*.95f,std::sin(a)*.95f);}break;
    case 5:line(-.6f,-.6f,0,0);line(0,0,.65f,-.4f);line(0,0,.2f,.75f);d->AddCircleFilled(p(-.6f,-.6f),3,color);d->AddCircleFilled(p(.65f,-.4f),3,color);d->AddCircleFilled(p(.2f,.75f),3,color);d->AddCircleFilled(c,3,color);break;
    case 6:d->AddCircle(c,size*.36f,color,20,t);d->AddTriangleFilled(p(.8f,-.65f),p(.1f,-.8f),p(.7f,.05f),color);break;
    case 7:d->AddTriangleFilled(p(-.5f,-.8f),p(-.5f,.8f),p(.85f,0),color);break;
    case 8:d->AddRectFilled(p(-.65f,-.8f),p(-.15f,.8f),color,1);d->AddRectFilled(p(.15f,-.8f),p(.65f,.8f),color,1);break;
    case 9:d->AddTriangleFilled(p(-.7f,-.75f),p(-.7f,.75f),p(.45f,0),color);d->AddRectFilled(p(.55f,-.8f),p(.85f,.8f),color,1);break;
    case 10:d->AddRectFilled(p(-.65f,-.65f),p(.65f,.65f),color,2);break;
    case 11:case 12:{float sign=icon==11?1.f:-1.f;line(-.75f*sign,-.1f,.2f*sign,-.1f);line(.2f*sign,-.1f,.65f*sign,.3f);line(.65f*sign,.3f,.65f*sign,.75f);line(-.75f*sign,-.1f,-.2f*sign,-.6f);line(-.75f*sign,-.1f,-.2f*sign,.4f);break;}
    case 13:d->AddCircle(p(0,-.35f),size*.2f,color,16,t);d->AddBezierQuadratic(p(-.7f,.7f),p(0,-.4f),p(.7f,.7f),color,t);break;
    case 14:line(-.55f,-.2f,0,.35f);line(0,.35f,.55f,-.2f);break;
    default:d->AddCircle(c,size*.3f,color,16,t);break;
    }
}
inline bool Button(const char* id,const std::string& caption,const std::string& tooltip,const NcmaGuiItemV1& item) {
    ImGui::SetCursorScreenPos({item.rect[0],item.rect[1]});
    bool pressed=ImGui::InvisibleButton(id,{item.rect[2],item.rect[3]});
    auto* d=ImGui::GetWindowDrawList();const bool hover=ImGui::IsItemHovered(ImGuiHoveredFlags_AllowWhenDisabled);
    const bool active=ImGui::IsItemActive();const bool selected=item.minimum==2,accent=item.minimum==1;
    const ImVec2 a{item.rect[0],item.rect[1]},b{a.x+item.rect[2],a.y+item.rect[3]};
    if(selected||hover||active)d->AddRectFilled(a,b,active?IM_COL32(26,65,102,255):IM_COL32(25,44,65,255),5);
    if(selected)d->AddRectFilled({a.x+7,b.y-2},{b.x-7,b.y},IM_COL32(39,151,248,255),1);
    ImU32 color=!item.enabled?IM_COL32(78,96,116,255):accent?IM_COL32(49,184,255,255):IM_COL32(176,197,220,255);
    Icon(d,{a.x+item.rect[2]*.5f,a.y+17},20,static_cast<unsigned>(item.value),color);
    if(item.rect[2]>=40){const float font=13;float text=ImGui::GetFont()->CalcTextSizeA(font,FLT_MAX,0,caption.c_str()).x;
        d->AddText(ImGui::GetFont(),font,{a.x+(item.rect[2]-text)*.5f,a.y+33},item.enabled?IM_COL32(203,217,233,255):IM_COL32(94,109,127,255),caption.c_str());}
    if(hover&&!tooltip.empty())ImGui::SetTooltip("%s",tooltip.c_str());
    return pressed&&item.enabled;
}
}
