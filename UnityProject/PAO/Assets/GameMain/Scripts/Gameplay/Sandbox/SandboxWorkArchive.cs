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
                if (unreadable > 0) warning = unreadable + " 份存档暂时无法读取，原文件已保留。";
                works.Sort((a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            }
            catch (Exception e) when (Handled(e)) { warning = "无法打开作品存档，请检查存储空间或访问权限。"; }
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
            catch (Exception e) when (Handled(e)) { error = "保存失败，请检查存储空间或访问权限。"; return false; }
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
            catch (Exception e) when (Handled(e)) { error = "覆盖失败，原作品存档已保留。"; return false; }
        }
        public bool Load(BubbleSandbox world, string id, out string error)
        {
            error = null;
            try
            {
                Record record = Read(id);
                if (!world.RestoreWorldJson(record.worldJson, true))
                { error = "存档内容无效，当前作品已保留。"; return false; }
                world.SetCurrentWork(record.id, record.name);
                world.Notify("已读取作品「" + record.name + "」 · " + world.Count + " 件造物");
                return true;
            }
            catch (Exception e) when (Handled(e)) { error = "读取失败，当前作品已保留。"; return false; }
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
            catch (Exception e) when (Handled(e)) { error = "重命名失败，原作品存档已保留。"; return false; }
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
            catch (Exception e) when (Handled(e)) { error = "删除失败，请检查存档访问权限。"; return false; }
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
            if (normalized.Length == 0) { error = "请给作品起个名字。"; return false; }
            if (normalized.Length > NameLimit) { error = "作品名称最多 " + NameLimit + " 个字符。"; return false; }
            foreach (char character in normalized)
                if (char.IsControl(character)) { error = "作品名称不能包含换行或控制字符。"; return false; }
            return true;
        }
    }
}
