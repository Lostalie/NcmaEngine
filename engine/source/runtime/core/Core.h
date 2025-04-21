#pragma once

#ifdef PLATFORM_WINDOWS
	
#ifdef BUILD_DLL
#define CORE_API __declspec(dllexport)
#else
#define CORE_API __declspec(dllimport)
#endif // BUILD_DLL

#else

#error Engine Only Support Windows

#endif // PLATFORM_WINDOWS


