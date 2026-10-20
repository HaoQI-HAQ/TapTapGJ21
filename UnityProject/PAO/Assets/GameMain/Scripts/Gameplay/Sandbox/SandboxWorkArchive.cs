using System;
using System.Collections.Generic;
using System.IO;
using PAO.UI.V2;
using UnityEngine;

namespace PAO.Sandbox
{
    public sealed class SandboxWorkInfo
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public int CellCount { get; internal set; }
        public DateTime UpdatedUtc { get; internal set; }
    }

    /// <summary>Each named work is one atomic snapshot, independent of the rolling autosave.</summary>
    public sealed class SandboxWorkArchive
    {
        public const int NameLimit = 32;
        public string DirectoryPath { get; }
        [Serializable]
        private sealed class Record
        {
            public int version = 1;
            public string id, name, worldJson;
            public int cellCount;
            public long updatedUtcTicks;
        }
        public SandboxWorkArchive(string directory = null)
        { DirectoryPath = directory ?? Path.Combine(Application.persistentDataPath, "Creat", "Works"); }

        public List<SandboxWorkInfo> List(out string warning)
        {
            warning = null;
            var works = new List<SandboxWorkInfo>();
            try
            {
                if (!Directory.Exists(DirectoryPath)) return works;
                int unreadable = 0;
                foreach (string file in Directory.GetFiles(DirectoryPath, "*.json"))
                {
                    try { works.Add(Info(Read(Path.GetFileNameWithoutExtension(file)))); }
                    catch (Exception e) when (Handled(e)) { unreadable++; }
                }
                if (unreadable > 0) warning = string.Format(UISettings.Text("{0} 份存档暂时无法读取，原文件已保留。", "{0} saved works could not be read. Their files have been kept."), unreadable);
                works.Sort((a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            }
            catch (Exception e) when (Handled(e)) { warning = UISettings.Text("无法打开作品存档，请检查存储空间或访问权限。", "Cannot open saved works. Check storage space and access permissions."); }
            return works;
        }

        public bool SaveNew(BubbleSandbox world, string name, out SandboxWorkInfo info, out string error)
        {
            info = null;
            if (!ValidateName(name, out name, out error)) return false;
            try
            {
                var record = new Record { id = Guid.NewGuid().ToString("N"), name = name,
                    cellCount = world.Count, worldJson = world.CaptureWorldJson(), updatedUtcTicks = DateTime.UtcNow.Ticks };
                Write(record); info = Info(record); world.SetCurrentWork(record.id, record.name); return true;
            }
            catch (Exception e) when (Handled(e)) { error = UISettings.Text("保存失败，请检查存储空间或访问权限。", "Save failed. Check storage space and access permissions."); return false; }
        }
        public bool Overwrite(BubbleSandbox world, string id, out string error)
        {
            error = null;
            try
            {
                Record record = Read(id);
                record.worldJson = world.CaptureWorldJson(); record.cellCount = world.Count;
                record.updatedUtcTicks = DateTime.UtcNow.Ticks;
                Write(record); world.SetCurrentWork(record.id, record.name); return true;
            }
            catch (Exception e) when (Handled(e)) { error = UISettings.Text("覆盖失败，原作品存档已保留。", "Overwrite failed. The original saved work has been kept."); return false; }
        }
        public bool Load(BubbleSandbox world, string id, out string error)
        {
            error = null;
            try
            {
                Record record = Read(id);
                if (!world.RestoreWorldJson(record.worldJson, true))
                { error = UISettings.Text("存档内容无效，当前作品已保留。", "Invalid saved work. Your current work has been kept."); return false; }
                world.SetCurrentWork(record.id, record.name);
                world.Notify("已读取作品「{0}」 · {1} 件造物", "Loaded \"{0}\" · {1} objects", record.name, world.Count);
                return true;
            }
            catch (Exception e) when (Handled(e)) { error = UISettings.Text("读取失败，当前作品已保留。", "Load failed. Your current work has been kept."); return false; }
        }
        public bool Rename(string id, string name, out string error)
        {
            if (!ValidateName(name, out name, out error)) return false;
            try
            {
                Record record = Read(id); record.name = name;
                Write(record);
                BubbleSandbox current = BubbleSandbox.Instance;
                if (current != null && current.CurrentWorkId == id) current.SetCurrentWork(id, name);
                return true;
            }
            catch (Exception e) when (Handled(e)) { error = UISettings.Text("重命名失败，原作品存档已保留。", "Rename failed. The original saved work has been kept."); return false; }
        }
        public bool Delete(string id, out string error)
        {
            error = null;
            try
            {
                string path = WorkPath(id);
                // Remove auxiliary files too, so a deleted work cannot return as an old backup.
                File.Delete(path + ".bak"); File.Delete(path + ".tmp"); File.Delete(path);
                return true;
            }
            catch (Exception e) when (Handled(e)) { error = UISettings.Text("删除失败，请检查存档访问权限。", "Delete failed. Check access permissions for the saved work."); return false; }
        }
        private string WorkPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid work id.");
            return Path.Combine(DirectoryPath, id + ".json");
        }
        private Record Read(string id)
        {
            Record record = JsonUtility.FromJson<Record>(AtomicJsonFile.Read(WorkPath(id)));
            if (record == null || record.version != 1 || record.id != id ||
                !ValidateName(record.name, out _, out _) || string.IsNullOrEmpty(record.worldJson) ||
                record.cellCount < 0 || record.updatedUtcTicks <= 0 || record.updatedUtcTicks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("Invalid work archive.");
            return record;
        }
        private void Write(Record record) => AtomicJsonFile.Write(WorkPath(record.id), JsonUtility.ToJson(record));
        private static SandboxWorkInfo Info(Record record) => new SandboxWorkInfo { Id = record.id,
            Name = record.name, CellCount = record.cellCount, UpdatedUtc = new DateTime(record.updatedUtcTicks, DateTimeKind.Utc) };
        private static bool Handled(Exception e) => e is IOException || e is InvalidDataException ||
            e is UnauthorizedAccessException || e is ArgumentException;
        public static bool ValidateName(string value, out string normalized, out string error)
        {
            normalized = (value ?? "").Trim(); error = null;
            if (normalized.Length == 0) { error = UISettings.Text("请给作品起个名字。", "Enter a name for your work."); return false; }
            if (normalized.Length > NameLimit) { error = string.Format(UISettings.Text("作品名称最多 {0} 个字符。", "Work names can contain up to {0} characters."), NameLimit); return false; }
            foreach (char character in normalized)
                if (char.IsControl(character)) { error = UISettings.Text("作品名称不能包含换行或控制字符。", "Work names cannot contain line breaks or control characters."); return false; }
            return true;
        }
    }
}
