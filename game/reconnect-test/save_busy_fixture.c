#include <windows.h>
#ifdef INVALID_BUSY_EXPORT
__declspec(dllexport) void PawSaveBusy(void) { }
#else
__declspec(dllexport) volatile LONG PawSaveBusy = 0;
#endif
