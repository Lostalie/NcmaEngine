// White-box failure seam exists only in this test executable, never in the DLL.
#include <windows.h>
#define GLFW_INCLUDE_NONE
#include <GLFW/glfw3.h>
static bool mismatchFramebuffer=false;
static void TestFramebufferSize(GLFWwindow* pointer,int* width,int* height)
{
    glfwGetFramebufferSize(pointer,width,height);
    if(mismatchFramebuffer&&height)++*height;
}
#define glfwGetFramebufferSize TestFramebufferSize
#include "../../engine/source/plugins/platform/PlatformPlugin.cpp"
#undef glfwGetFramebufferSize
#include <iostream>
#include <stdexcept>
static int checks=0;
static void Check(bool value)
{
    ++checks;
    if(!value)throw std::runtime_error("Pixel window check "+std::to_string(checks));
}
int main()
{
    try {
        NcmaErrorV1 error{};NcmaPlatformApiV1_2 api{};
        Check(ncma_plugin_get_api(1,2,&api,sizeof(api),&error)==NCMA_OK);
        Check(api.base.base.module.minor==2&&api.base.base.module.struct_size==152);
        Check(ncma_plugin_get_api(1,2,&api,144,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==152);
        Check(ncma_plugin_get_api(1,3,&api,sizeof(api),&error)==NCMA_ABI_MISMATCH);
        Check(ncma_plugin_get_api(2,2,&api,sizeof(api),&error)==NCMA_ABI_MISMATCH);
        uint64_t context=0,identity=123;
        Check(api.base.base.module.initialize(nullptr,0,&context,&error)==NCMA_OK);
        const std::string title="Pixel window contract tests";
        NcmaPixelWindowDescriptionV1 description{{sizeof(description),1920,1080,0,
            reinterpret_cast<const uint8_t*>(title.data()),static_cast<uint32_t>(title.size()),0},NCMA_BORDERLESS_PIXELS,0};
        auto status=[&](uint64_t expected) {
            NcmaModuleStatusV1 state{};
            Check(api.base.base.module.get_status(context,&state,&error)==NCMA_OK&&state.live_resources==expected);
        };
        Check(api.create_pixel_window(context,nullptr,&identity,&error)==NCMA_INVALID_ARGUMENT&&identity==123);
        Check(api.create_pixel_window(context,&description,nullptr,&error)==NCMA_INVALID_ARGUMENT);status(0);
        Check(api.create_pixel_window(0,&description,&identity,&error)==NCMA_INVALID_HANDLE&&identity==123);
        uint32_t wrong=NCMA_OK;
        std::thread worker([&]{NcmaErrorV1 local{};wrong=api.create_pixel_window(context,&description,&identity,&local);});worker.join();
        Check(wrong==NCMA_WRONG_THREAD&&identity==123);
        for(unsigned test=0;test<8;++test) {
            auto bad=description;
            switch(test) {
                case 0:bad.window.struct_size=32;break;
                case 1:bad.mode=0;break;
                case 2:bad.mode=2;break;
                case 3:bad.reserved=1;break;
                case 4:bad.window.reserved=1;break;
                case 5:bad.window.width=0;break;
                case 6:bad.window.height=16385;break;
                case 7:bad.window.visible=2;break;
            }
            Check(api.create_pixel_window(context,&bad,&identity,&error)==NCMA_INVALID_ARGUMENT&&identity==123);status(0);
        }
        mismatchFramebuffer=true;
        Check(api.create_pixel_window(context,&description,&identity,&error)==NCMA_UNSUPPORTED_FEATURE&&identity==0);
        mismatchFramebuffer=false;status(0);Check(window==nullptr);
        uint64_t timer=0;
        Check(api.base.create_precision_wait(context,&timer,&error)==NCMA_OK);status(1);
        for(const auto size:{std::array<uint32_t,2>{1920,1080},std::array<uint32_t,2>{853,479}}) {
            description.window.width=size[0];description.window.height=size[1];
            Check(api.create_pixel_window(context,&description,&identity,&error)==NCMA_OK&&identity!=0);status(2);
            GLFWwindow* pointer=nullptr;
            Check(ncma_platform_borrow_window_v1(context,identity,&pointer,&error)==NCMA_OK);
            Check(glfwGetWindowAttrib(pointer,GLFW_DECORATED)==GLFW_FALSE&&
                glfwGetWindowAttrib(pointer,GLFW_RESIZABLE)==GLFW_FALSE&&glfwGetWindowAttrib(pointer,GLFW_VISIBLE)==GLFW_FALSE);
            int x=0,y=0,primaryX=0,primaryY=0;
            glfwGetWindowPos(pointer,&x,&y);glfwGetMonitorPos(glfwGetPrimaryMonitor(),&primaryX,&primaryY);
            Check(x==primaryX&&y==primaryY);
            Check(api.base.precision_wait(context,timer,1,&error)==NCMA_OK);
            NcmaWindowStateV1 state{};
            Check(api.base.base.get_window_state(context,identity,&state,&error)==NCMA_OK&&
                state.framebuffer_width==size[0]&&state.framebuffer_height==size[1]);
            uint64_t second=456;
            Check(api.create_pixel_window(context,&description,&second,&error)==NCMA_BUSY&&second==456);
            Check(api.base.base.destroy_window(context,identity,&error)==NCMA_BUSY);
            Check(ncma_platform_release_window_v1(context,identity,&error)==NCMA_OK);
            Check(api.base.base.destroy_window(context,identity,&error)==NCMA_OK);status(1);
            Check(api.base.base.get_window_state(context,identity,&state,&error)==NCMA_INVALID_HANDLE);
        }
        Check(api.base.destroy_precision_wait(context,timer,&error)==NCMA_OK);status(0);
        auto ordinary=description.window;ordinary.struct_size=sizeof(ordinary);
        Check(api.base.base.create_window(context,&ordinary,&identity,&error)==NCMA_OK);
        GLFWwindow* pointer=nullptr;
        Check(ncma_platform_borrow_window_v1(context,identity,&pointer,&error)==NCMA_OK);
        Check(glfwGetWindowAttrib(pointer,GLFW_DECORATED)==GLFW_TRUE&&glfwGetWindowAttrib(pointer,GLFW_RESIZABLE)==GLFW_TRUE);
        Check(ncma_platform_release_window_v1(context,identity,&error)==NCMA_OK);
        Check(api.base.base.destroy_window(context,identity,&error)==NCMA_OK);status(0);
        Check(api.base.base.module.shutdown(context,&error)==NCMA_OK);
        std::cout<<"PASS Platform1.2 pixel windows "<<checks<<" checks; injected mismatch cleaned; no performance acceptance\n";
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
