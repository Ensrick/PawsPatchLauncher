/* Appended only in the Pure recovery beta's private native/managed contract. */
typedef struct {
    DWORD mainThread, autoVote;
    wchar_t data[MAX_PATH], log[MAX_PATH];
} PawRecoveryConfig;
DWORD PawRecoveryPrepare(const PawRecoveryConfig *config);
DWORD PawRecoveryInstall(const PawRecoveryConfig *config, uintptr_t image);
