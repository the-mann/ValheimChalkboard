using System;
using HarmonyLib;

namespace ValheimWhiteboard
{
    internal static class MigrationPersistence
    {
        internal static void Save()
        {
            uint before = SaveSystem.GetSaveNumber();
            AccessTools.Method(typeof(ZNet), "SaveWorld", new[] {typeof(bool)}).Invoke(ZNet.instance, new object[] {true});
            // SaveSystem advances this number only after a successful disk commit.
            // A failed or skipped save must never open the server to vanilla clients.
            if (SaveSystem.GetSaveNumber() == before)
                throw new InvalidOperationException("Migrated world did not commit; server remains closed.");
            WhiteboardPlugin.LogInfo("Migrated world committed before opening server.");
        }
    }
}
