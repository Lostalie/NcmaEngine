#pragma once

// Simple OpenGL loader for Windows using wglGetProcAddress
// This is a minimal loader for our renderer

#define GLAPI extern
#define GLAPIENTRY __stdcall

// OpenGL types
typedef unsigned int GLenum;
typedef unsigned int GLuint;
typedef int GLint;
typedef int GLsizei;
typedef float GLfloat;
typedef double GLdouble;
typedef unsigned char GLubyte;
typedef unsigned short GLushort;
typedef char GLchar;
typedef void GLvoid;
typedef unsigned int GLbitfield;
typedef int64_t GLint64;
typedef uint64_t GLuint64;
typedef int GLsizeiptr;

// OpenGL enums
#define GL_FALSE 0
#define GL_TRUE 1
#define GL_DEPTH_BUFFER_BIT 0x00000100
#define GL_COLOR_BUFFER_BIT 0x00004000
#define GL_LESS 0x0201
#define GL_SRC_ALPHA 0x0302
#define GL_ONE_MINUS_SRC_ALPHA 0x0303
#define GL_CULL_FACE 0x0B44
#define GL_BACK 0x0405
#define GL_TRIANGLES 0x0004
#define GL_UNSIGNED_INT 0x1405
#define GL_FLOAT 0x1406
#define GL_INT 0x1404
#define GL_BOOL 0x8B56
#define GL_ARRAY_BUFFER 0x8892
#define GL_ELEMENT_ARRAY_BUFFER 0x8893
#define GL_STATIC_DRAW 0x88E4
#define GL_DYNAMIC_DRAW 0x88E8
#define GL_VERTEX_SHADER 0x8B31
#define GL_FRAGMENT_SHADER 0x8B30
#define GL_CURRENT_PROGRAM 0x8259
#define GL_VIEWPORT 0x0BA2
#define GL_BLEND 0x0BE2
#define GL_DEPTH_TEST 0x0B71
#define GL_NEAREST 0x2600
#define GL_LINEAR 0x2601

// OpenGL function pointer types
typedef void (GLAPIENTRY* PFNGLGENBUFFERSPROC)(GLsizei n, GLuint* buffers);
typedef void (GLAPIENTRY* PFNGLDELETEBUFFERSPROC)(GLsizei n, const GLuint* buffers);
typedef void (GLAPIENTRY* PFNGLBINDBUFFERPROC)(GLenum target, GLuint buffer);
typedef void (GLAPIENTRY* PFNGLBUFFERDATAPROC)(GLenum target, GLsizeiptr size, const void* data, GLenum usage);
typedef void (GLAPIENTRY* PFNGLBUFFERSUBDATAPROC)(GLenum target, GLintptr offset, GLsizeiptr size, const void* data);
typedef void (GLAPIENTRY* PFNGLENABLEVERTEXATTRIBARRAYPROC)(GLuint index);
typedef void (GLAPIENTRY* PFNGLDISABLEVERTEXATTRIBARRAYPROC)(GLuint index);
typedef void (GLAPIENTRY* PFNGLVERTEXATTRIBPOINTERPROC)(GLuint index, GLint size, GLenum type, GLboolean normalized, GLsizei stride, const void* pointer);
typedef void (GLAPIENTRY* PFNGLGENVERTEXARRAYSPROC)(GLsizei n, GLuint* arrays);
typedef void (GLAPIENTRY* PFNGLDELETEVERTEXARRAYSPROC)(GLsizei n, const GLuint* arrays);
typedef void (GLAPIENTRY* PFNGLBINDVERTEXARRAYPROC)(GLuint array);
typedef GLuint (GLAPIENTRY* PFNGLCREATEPROGRAMPROC)(void);
typedef GLuint (GLAPIENTRY* PFNGLCREATESHADERPROC)(GLenum type);
typedef void (GLAPIENTRY* PFNGLSHADERSOURCEPROC)(GLuint shader, GLsizei count, const GLchar* const* string, const GLint* length);
typedef void (GLAPIENTRY* PFNGLCOMPILESHADERPROC)(GLuint shader);
typedef void (GLAPIENTRY* PFNGLATTACHSHADERPROC)(GLuint program, GLuint shader);
typedef void (GLAPIENTRY* PFNGLDETACHSHADERPROC)(GLuint program, GLuint shader);
typedef void (GLAPIENTRY* PFNGLLINKPROGRAMPROC)(GLuint program);
typedef void (GLAPIENTRY* PFNGLUSEPROGRAMPROC)(GLuint program);
typedef void (GLAPIENTRY* PFNGLDELETEPROGRAMPROC)(GLuint program);
typedef void (GLAPIENTRY* PFNGLDELETESHADERPROC)(GLuint shader);
typedef GLint (GLAPIENTRY* PFNGLGETUNIFORMLOCATIONPROC)(GLuint program, const GLchar* name);
typedef void (GLAPIENTRY* PFNGLUNIFORM1IPROC)(GLint location, GLint v0);
typedef void (GLAPIENTRY* PFNGLUNIFORM1FPROC)(GLint location, GLfloat v0);
typedef void (GLAPIENTRY* PFNGLUNIFORM2FPROC)(GLint location, GLfloat v0, GLfloat v1);
typedef void (GLAPIENTRY* PFNGLUNIFORM3FPROC)(GLint location, GLfloat v0, GLfloat v1, GLfloat v2);
typedef void (GLAPIENTRY* PFNGLUNIFORM4FPROC)(GLint location, GLfloat v0, GLfloat v1, GLfloat v2, GLfloat v3);
typedef void (GLAPIENTRY* PFNGLUNIFORMMATRIX4FVPROC)(GLint location, GLsizei count, GLboolean transpose, const GLfloat* value);
typedef void (GLAPIENTRY* PFNGLCLEARPROC)(GLenum mask);
typedef void (GLAPIENTRY* PFNGLCLEARCOLORPROC)(GLfloat red, GLfloat green, GLfloat blue, GLfloat alpha);
typedef void (GLAPIENTRY* PFNGLVIEWPORTPROC)(GLint x, GLint y, GLsizei width, GLsizei height);
typedef void (GLAPIENTRY* PFNGLENABLEPROC)(GLenum cap);
typedef void (GLAPIENTRY* PFNGLDISABLEPROC)(GLenum cap);
typedef void (GLAPIENTRY* PFNGLCULLFACEPROC)(GLenum mode);
typedef void (GLAPIENTRY* PFNGLGETSHADERIVPROC)(GLuint shader, GLenum pname, GLint* params);
typedef void (GLAPIENTRY* PFNGLGETPROGRAMIVPROC)(GLuint program, GLenum pname, GLint* params);
typedef void (GLAPIENTRY* PFNGLGETSHADERINFOLOGPROC)(GLuint shader, GLsizei bufSize, GLsizei* length, GLchar* infoLog);
typedef void (GLAPIENTRY* PFNGLGETPROGRAMINFOLOGPROC)(GLuint program, GLsizei bufSize, GLsizei* length, GLchar* infoLog);
typedef GLenum (GLAPIENTRY* PFNGLGETERRORPROC)(void);
typedef const GLubyte* (GLAPIENTRY* PFNGLGETSTRINGPROC)(GLenum name);

// OpenGL function pointers (loaded at runtime)
extern PFNGLGENBUFFERSPROC glGenBuffers;
extern PFNGLDELETEBUFFERSPROC glDeleteBuffers;
extern PFNGLBINDBUFFERPROC glBindBuffer;
extern PFNGLBUFFERDATAPROC glBufferData;
extern PFNGLBUFFERSUBDATAPROC glBufferSubData;
extern PFNGLENABLEVERTEXATTRIBARRAYPROC glEnableVertexAttribArray;
extern PFNGLDISABLEVERTEXATTRIBARRAYPROC glDisableVertexAttribArray;
extern PFNGLVERTEXATTRIBPOINTERPROC glVertexAttribPointer;
extern PFNGLGENVERTEXARRAYSPROC glGenVertexArrays;
extern PFNGLDELETEVERTEXARRAYSPROC glDeleteVertexArrays;
extern PFNGLBINDVERTEXARRAYPROC glBindVertexArray;
extern PFNGLCREATEPROGRAMPROC glCreateProgram;
extern PFNGLCREATESHADERPROC glCreateShader;
extern PFNGLSHADERSOURCEPROC glShaderSource;
extern PFNGLCOMPILESHADERPROC glCompileShader;
extern PFNGLATTACHSHADERPROC glAttachShader;
extern PFNGLDETACHSHADERPROC glDetachShader;
extern PFNGLLINKPROGRAMPROC glLinkProgram;
extern PFNGLUSEPROGRAMPROC glUseProgram;
extern PFNGLDELETEPROGRAMPROC glDeleteProgram;
extern PFNGLDELETESHADERPROC glDeleteShader;
extern PFNGLGETUNIFORMLOCATIONPROC glGetUniformLocation;
extern PFNGLUNIFORM1IPROC glUniform1i;
extern PFNGLUNIFORM1FPROC glUniform1f;
extern PFNGLUNIFORM2FPROC glUniform2f;
extern PFNGLUNIFORM3FPROC glUniform3f;
extern PFNGLUNIFORM4FPROC glUniform4f;
extern PFNGLUNIFORMMATRIX4FVPROC glUniformMatrix4fv;
extern PFNGLCLEARPROC glClear;
extern PFNGLCLEARCOLORPROC glClearColor;
extern PFNGLVIEWPORTPROC glViewport;
extern PFNGLENABLEPROC glEnable;
extern PFNGLDISABLEPROC glDisable;
extern PFNGLCULLFACEPROC glCullFace;
extern PFNGLGETSHADERIVPROC glGetShaderiv;
extern PFNGLGETPROGRAMIVPROC glGetProgramiv;
extern PFNGLGETSHADERINFOLOGPROC glGetShaderInfoLog;
extern PFNGLGETPROGRAMINFOLOGPROC glGetProgramInfoLog;
extern PFNGLGETERRORPROC glGetError;
extern PFNGLGETSTRINGPROC glGetString;

// Initialize OpenGL function pointers
bool InitOpenGL(void* (*getProcAddress)(const char*));
