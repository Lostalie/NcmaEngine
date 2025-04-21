
#include <windows.h>
#include <stdio.h>

#include "../core/CoreGlobal.h"
#include "../engine/Engine.h"


namespace Launcher
{
	bool IsEngineExit()
	{
		return GExitEngine;
	}

	void EngineTick(void)
	{

	}


	int LaunchStartup(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd)
	{
		printf("Start Engine");
		int ErrorCode = 0;
		auto engine = NcmaEngine::CreateEngine();
		while (!IsEngineExit())
		{
			EngineTick();
		}
		return ErrorCode;
	}

	void LaunchShutdown()
	{

	}
}

extern NcmaEngine::Engine* NcmaEngine::CreateEngine();

int WINAPI WinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ LPSTR lpCmdLine, _In_ int nShowCmd)
{

	int Result = Launcher::LaunchStartup(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
	Launcher::LaunchShutdown();
	return 0;
}