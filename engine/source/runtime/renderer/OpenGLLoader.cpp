#include "OpenGLLoader.h"
#include <windows.h>

// Define OpenGL function pointers
PFNGLGENBUFFERSPROC glGenBuffers = nullptr;
PFNGLDELETEBUFFERSPROC glDeleteBuffers = nullptr;
PFNGLBINDBUFFERPROC glBindBuffer = nullptr;
PFNGLBUFFERDATAPROC glBufferData = nullptr;
PFNGLBUFFERSUBDATAPROC glBufferSubData = nullptr;
PFNGLENABLEVERTEXATTRIBARRAYPROC glEnableVertexAttribArray = nullptr;
PFNGLDISABLEVERTEXATTRIBARRAYPROC glDisableVertexAttribArray = nullptr;
PFNGLVERTEXATTRIBPOINTERPROC glVertexAttribPointer = nullptr;
PFNGLGENVERTEXARRAYSPROC glGenVertexArrays = nullptr;
PFNGLDELETEVERTEXARRAYSPROC glDeleteVertexArrays = nullptr;
PFNGLBINDVERTEXARRAYPROC glBindVertexArray = nullptr;
PFNGLCREATEPROGRAMPROC glCreateProgram = nullptr;
PFNGLCREATESHADERPROC glCreateShader = nullptr;
PFNGLSHADERSOURCEPROC glShaderSource = nullptr;
PFNGLCOMPILESHADERPROC glCompileShader = nullptr;
PFNGLATTACHSHADERPROC glAttachShader = nullptr;
PFNGLDETACHSHADERPROC glDetachShader = nullptr;
PFNGLLINKPROGRAMPROC glLinkProgram = nullptr;
PFNGLUSEPROGRAMPROC glUseProgram = nullptr;
PFNGLDELETEPROGRAMPROC glDeleteProgram = nullptr;
PFNGLDELETESHADERPROC glDeleteShader = nullptr;
PFNGLGETUNIFORMLOCATIONPROC glGetUniformLocation = nullptr;
PFNGLUNIFORM1IPROC glUniform1i = nullptr;
PFNGLUNIFORM1FPROC glUniform1f = nullptr;
PFNGLUNIFORM2FPROC glUniform2f = nullptr;
PFNGLUNIFORM3FPROC glUniform3f = nullptr;
PFNGLUNIFORM4FPROC glUniform4f = nullptr;
PFNGLUNIFORMMATRIX4FVPROC glUniformMatrix4fv = nullptr;
PFNGLCLEARPROC glClear = nullptr;
PFNGLCLEARCOLORPROC glClearColor = nullptr;
PFNGLVIEWPORTPROC glViewport = nullptr;
PFNGLENABLEPROC glEnable = nullptr;
PFNGLDISABLEPROC glDisable = nullptr;
PFNGLCULLFACEPROC glCullFace = nullptr;
PFNGLGETSHADERIVPROC glGetShaderiv = nullptr;
PFNGLGETPROGRAMIVPROC glGetProgramiv = nullptr;
PFNGLGETSHADERINFOLOGPROC glGetShaderInfoLog = nullptr;
PFNGLGETPROGRAMINFOLOGPROC glGetProgramInfoLog = nullptr;
PFNGLGETERRORPROC glGetError = nullptr;
PFNGLGETSTRINGPROC glGetString = nullptr;

#define LOAD_FUNC(func) \
    func = (decltype(func))getProcAddress(#func); \
    if (!func) return false;

bool InitOpenGL(void* (*getProcAddress)(const char*))
{
    // Core OpenGL functions
    LOAD_FUNC(glGenBuffers);
    LOAD_FUNC(glDeleteBuffers);
    LOAD_FUNC(glBindBuffer);
    LOAD_FUNC(glBufferData);
    LOAD_FUNC(glBufferSubData);
    LOAD_FUNC(glEnableVertexAttribArray);
    LOAD_FUNC(glDisableVertexAttribArray);
    LOAD_FUNC(glVertexAttribPointer);
    LOAD_FUNC(glGenVertexArrays);
    LOAD_FUNC(glDeleteVertexArrays);
    LOAD_FUNC(glBindVertexArray);
    LOAD_FUNC(glCreateProgram);
    LOAD_FUNC(glCreateShader);
    LOAD_FUNC(glShaderSource);
    LOAD_FUNC(glCompileShader);
    LOAD_FUNC(glAttachShader);
    LOAD_FUNC(glDetachShader);
    LOAD_FUNC(glLinkProgram);
    LOAD_FUNC(glUseProgram);
    LOAD_FUNC(glDeleteProgram);
    LOAD_FUNC(glDeleteShader);
    LOAD_FUNC(glGetUniformLocation);
    LOAD_FUNC(glUniform1i);
    LOAD_FUNC(glUniform1f);
    LOAD_FUNC(glUniform2f);
    LOAD_FUNC(glUniform3f);
    LOAD_FUNC(glUniform4f);
    LOAD_FUNC(glUniformMatrix4fv);
    LOAD_FUNC(glClear);
    LOAD_FUNC(glClearColor);
    LOAD_FUNC(glViewport);
    LOAD_FUNC(glEnable);
    LOAD_FUNC(glDisable);
    LOAD_FUNC(glCullFace);
    LOAD_FUNC(glGetShaderiv);
    LOAD_FUNC(glGetProgramiv);
    LOAD_FUNC(glGetShaderInfoLog);
    LOAD_FUNC(glGetProgramInfoLog);
    LOAD_FUNC(glGetError);
    LOAD_FUNC(glGetString);

    return true;
}
