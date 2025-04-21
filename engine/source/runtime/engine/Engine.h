#pragma once


namespace NcmaEngine
{
	class Engine {

		virtual void Tick(float DeltaSeconds, bool bIdleMode);
	};


	Engine* CreateEngine();
}