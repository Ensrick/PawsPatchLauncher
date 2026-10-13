/* Regression for a real 16 ms save missed by the 50 ms message-pump poll.
 * Real temporary files and sharing locks; engine state/commands are fixtures. */
#define PAW_TEST
#define main upstream_main
#include "multiplayer_saves.c"
#undef main

static HANDLE writer(const wchar_t *path, unsigned bytes) {
    HANDLE h=CreateFileW(path,GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,0,CREATE_ALWAYS,0,0);
    char data[128]={0};DWORD written;
    check(h!=INVALID_HANDLE_VALUE,"open private file writer");
    if(h!=INVALID_HANDLE_VALUE)check(WriteFile(h,data,bytes,&written,0)&&written==bytes,"write private file");
    return h;
}
int main(void) {
    wchar_t root[MAX_PATH],save[MAX_PATH],a[MAX_PATH],b[MAX_PATH],leaf[64];HANDLE h;
    check(GetTempPathW(MAX_PATH,root)>0,"temporary root");
    swprintf(leaf,L"PawFastSave-%lu-%lu",GetCurrentProcessId(),GetTickCount());wcscat(root,leaf);
    check(CreateDirectoryW(root,0),"new private test directory");
    wcscpy(config.data,root);swprintf(save,L"%ls\\Save",root);check(CreateDirectoryW(save,0),"save directory");
    swprintf(a,L"%ls\\MP Autosave A.RSG",save);swprintf(b,L"%ls\\MP Autosave B.RSG",save);

    hosting();autosave_tick(62000);check(autosaveBaseline==0&&PawSaveBusy,"absent baseline captured before queue");
    h=writer(a,32);autosave_tick(62050);check(PawSaveBusy&&autosaveState==AUTOSAVE_QUEUED,"live writer cannot acknowledge a save");
    CloseHandle(h);autosave_tick(62100);check(!PawSaveBusy&&autosaveState==AUTOSAVE_IDLE,"closed new file acknowledges a task missed between polls");

    hosting();autosave_tick(62000);autosave_tick(62050);
    check(autosaveBaseline==1&&PawSaveBusy,"an unchanged old autosave is never completion");
    h=writer(a,64);autosave_tick(62100);check(PawSaveBusy,"replacement writer must close first");CloseHandle(h);
    autosave_tick(62150);check(!PawSaveBusy,"changed old slot acknowledged after close");

    hosting();autosave_tick(62000);h=writer(a,0);CloseHandle(h);autosave_tick(62050);
    check(PawSaveBusy,"empty changed output is not a finished save");
    h=writer(a,96);CloseHandle(h);*(void**)(session+SESSION_SAVE_TASK)=task1;autosave_tick(62100);
    check(PawSaveBusy&&autosaveState==AUTOSAVE_RUNNING,"native task takes precedence over the file fallback");
    *(void**)(session+SESSION_SAVE_TASK)=0;autosave_tick(62150);check(!PawSaveBusy,"observed task releases normally");

    h=writer(a,64);hosting();autosave_tick(62000);check(autosaveBaseline<0,"unreadable baseline is not absent");
    CloseHandle(h);autosave_tick(62050);check(PawSaveBusy,"unknown baseline cannot acknowledge an output");
    autosave_tick(92000);check(!PawSaveBusy,"unknown baseline still uses the bounded native timeout");

    DeleteFileW(a);DeleteFileW(b);RemoveDirectoryW(save);RemoveDirectoryW(root);
    printf("%s %u fast-save completion checks (real files and writer locks)\n",failed?"FAIL":"PASS",checks);
    return failed;
}
