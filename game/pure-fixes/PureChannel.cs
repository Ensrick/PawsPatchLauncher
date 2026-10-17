using System;
using System.IO;

namespace PawPureFixes
{
    // This file is included only in the scoped Vanilla/Immortals channel builds.
    // The shared R2 source supplies the exact same native code as Arcane Wars.
    internal static class PureChannel
    {
#if PAW_PURE_RECOVERY
        internal const string Channel = "beta", Version = "1.3.72-pure.15-beta.4", PatchVersion = "0.4.1-beta.4";
#elif PAW_PURE_EXTENDED
        internal const string Channel = "stable", Version = "1.3.72-pure.14", PatchVersion = "0.4.0";
#else
        internal const string Channel = "stable", Version = "1.3.72-pure.11", PatchVersion = "0.3.0";
#endif
#if PAW_PURE_FAST_TRANSFER
        internal const bool FastTransfer = true;
#else
        internal const bool FastTransfer = false;
#endif
#if PAW_PURE_COLORS
        internal const bool Colors = true;
#else
        internal const bool Colors = false;
#endif
#if PAW_PURE_SYNC
        internal const bool Bypass = true;
#else
        internal const bool Bypass = false;
#endif
        internal static string Features
        {
            get
            {
                string result = Program.Features.Replace("1.3.72-pure.2", Version)
                    .Replace("\"menuVersions\":false", "\"menuVersions\":true")
                    .Replace("\"fastSaveTransfer\":false", "\"fastSaveTransfer\":" + (FastTransfer ? "true" : "false"))
                    .Replace("\"colors\":false", "\"colors\":" + (Colors ? "true" : "false"))
                    .Replace("\"bypass\":false", "\"bypass\":" + (Bypass ? "true" : "false"))
                    .Replace("\"quiet\":true", "\"quiet\":true" + (Bypass ? ",\"syncDiagnosticsRevision\":1" : ""))
                    .Replace("\"stockSyncChecks\":true", "\"stockSyncChecks\":" + (Bypass ? "false" : "true"))
                    .Replace("{", "{\"channel\":\"" + Channel + "\",\"patchVersion\":\"" + PatchVersion + "\",\"nativeTransferRevision\":\"" + (FastTransfer ? "R2" : "stock") + "\",");
#if PAW_PURE_EXTENDED
                result = PureBetaRuntime.Describe(result);
#endif
#if PAW_PURE_RECOVERY
                result = result.TrimEnd('}') + ",\"matchRecoveryRevision\":3,\"saveSourceGuardRevision\":1,\"saveCoordinationRevision\":1,\"matchRecoveryMode\":\"save-lobby\",\"automaticDropVoting\":false}";
#endif
                return result;
            }
        }
        internal sealed class Installed
        {
            internal uint PureCave, TransferCave;
        }
        internal static bool IsReady(IPatchMemory memory, uint image)
        {
            if (!PurePatch.IsReady(memory, image)) return false;
#if PAW_PURE_FAST_TRANSFER
            try { PawFastTransfer.Validate(memory, image); }
            catch (InvalidDataException) { return false; }
#endif
            return true;
        }
        internal static Installed Install(IPatchMemory memory, uint image)
        {
            PurePatch.Validate(memory, image);
#if PAW_PURE_FAST_TRANSFER
            // Validate all R2 code and live protocol settings before either
            // feature allocates or writes to the fresh, suspended process.
            PawFastTransfer.Validate(memory, image);
#endif
            var installed = new Installed { PureCave = PurePatch.Install(memory, image) };
#if PAW_PURE_FAST_TRANSFER
            try { installed.TransferCave = PawFastTransfer.InstallCore(memory, image); }
            catch
            {
                PurePatch.Uninstall(memory, image, installed.PureCave);
                throw;
            }
#endif
            return installed;
        }
    }
}
