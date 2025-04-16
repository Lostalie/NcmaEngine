
#include <windows.h>

#include "../core/CoreGlobal.h"


bool IsEngineExit()
{
	return GExitEngine;
}

void EngineTick(void)
{

}


int LaunchStartup(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine,int nShowCmd)
{
	int ErrorCode = 0;
	while (!IsEngineExit())
	{
		EngineTick();
	}
	return ErrorCode;
}

void LaunchShutdown()
{

}

int WINAPI WinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ LPSTR lpCmdLine, _In_ int nShowCmd)
{

	int Result = LaunchStartup(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
	LaunchShutdown();
	return 0;
}