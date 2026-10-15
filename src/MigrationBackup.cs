using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace ValheimWhiteboard
{
    public static class MigrationBackup
    {
        public static void Write(IReadOnlyList<ZDO> records)
        {
            var folder = Path.Combine(Paths.ConfigPath, "ValheimChalkboard", "migration-backups");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".cbbackup");
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("ChalkboardMigrationBackupV2"); writer.Write(records.Count);
                foreach (var record in records)
                {
                    var packet = new ZPackage(); record.Serialize(packet); var bytes = packet.GetArray();
                    writer.Write(record.m_uid.ToString()); writer.Write(record.GetOwner());
                    var position = record.GetPosition(); writer.Write(position.x); writer.Write(position.y); writer.Write(position.z);
                    writer.Write(bytes.Length); writer.Write(bytes);
                }
                writer.Flush(); stream.Flush(true);
            }
            WhiteboardPlugin.LogInfo("Backed up legacy board records to " + file);
        }
    }
}
