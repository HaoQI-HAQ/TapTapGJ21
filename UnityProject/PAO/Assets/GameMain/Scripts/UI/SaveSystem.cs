using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.UI
{
    /// <summary>
    /// 一个存档槽的记录内容。用 JsonUtility 序列化，所以字段必须 public、不能是属性。
    /// </summary>
    [Serializable]
    public class SaveSlotData
    {
        /// <summary>槽位序号，从 0 开始。</summary>
        public int slot;

        /// <summary>展示用的记录名，例如「记录一」。留空则按槽位自动生成。</summary>
        public string displayName = string.Empty;

        /// <summary>写入时间，ISO 8601（<c>DateTime.UtcNow.ToString("o")</c>）。</summary>
        public string savedAtUtc = string.Empty;

        /// <summary>存档所在场景名，用于读档时切场景。</summary>
        public string sceneName = string.Empty;

        /// <summary>玩家坐标，用于「恢复位置」。</summary>
        public float posX;
        public float posY;
        public float posZ;

        /// <summary>存档时的进度描述，例如「第 3 关」。</summary>
        public string progress = string.Empty;

        /// <summary>是否是一条真实存在的记录。</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(savedAtUtc); }
        }

        /// <summary>把写入时间转成本地时间，解析失败则返回 null。</summary>
        public DateTime? SavedAtLocal
        {
            get
            {
                DateTime parsed;
                if (DateTime.TryParse(savedAtUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
                {
                    return parsed.ToLocalTime();
                }

                return null;
            }
        }

        /// <summary>「保存时间」栏要显示的文本。</summary>
        public string SavedAtText
        {
            get
            {
                DateTime? local = SavedAtLocal;
                return local.HasValue ? local.Value.ToString("yyyy-MM-dd HH:mm") : "—";
            }
        }

        /// <summary>「恢复位置」栏要显示的文本。</summary>
        public string PositionText
        {
            get
            {
                if (!IsValid)
                {
                    return "—";
                }

                return string.Format("{0} ({1:0.#}, {2:0.#}, {3:0.#})",
                    string.IsNullOrEmpty(sceneName) ? "未记录场景" : sceneName, posX, posY, posZ);
            }
        }
    }

    /// <summary>
    /// 存档读写。刻意做成不依赖 MonoBehaviour 的静态类，界面之外的任何地方都能调用。
    ///
    /// 存储位置：<c>Application.persistentDataPath/PAO_Saves/</c>，每个槽一个 <c>slotN.json</c>。
    /// 选择文件而不是 PlayerPrefs，是因为记录里以后会塞更多东西（气泡布局、地形状态），
    /// 用 JSON 便于查看、迁移与调试。
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>默认槽位数量。</summary>
        public const int DefaultSlotCount = 3;

        private const string kFolderName = "PAO_Saves";

        /// <summary>一个槽位被读取时抛出，参数是那份数据。</summary>
        public static event Action<SaveSlotData> SlotLoaded;

        /// <summary>存档目录（不存在则创建）。</summary>
        public static string SaveDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, kFolderName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return dir;
            }
        }

        /// <summary>某个槽位的文件路径。</summary>
        public static string PathForSlot(int slot)
        {
            return Path.Combine(SaveDirectory, "slot" + Mathf.Max(0, slot) + ".json");
        }

        /// <summary>这个槽位有没有记录。</summary>
        public static bool HasSlot(int slot)
        {
            try
            {
                return File.Exists(PathForSlot(slot));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] 检查槽位失败: " + e.Message);
                return false;
            }
        }

        /// <summary>读取一个槽位。不存在或损坏时返回 null。</summary>
        public static SaveSlotData LoadSlot(int slot)
        {
            try
            {
                string path = PathForSlot(slot);
                if (!File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                SaveSlotData data = JsonUtility.FromJson<SaveSlotData>(json);
                if (data == null || !data.IsValid)
                {
                    return null;
                }

                data.slot = slot;
                if (string.IsNullOrEmpty(data.displayName))
                {
                    data.displayName = DefaultNameForSlot(slot);
                }

                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] 读取槽位 " + slot + " 失败: " + e.Message);
                return null;
            }
        }

        /// <summary>写入一个槽位。返回是否成功。</summary>
        public static bool SaveSlot(SaveSlotData data)
        {
            if (data == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(data.savedAtUtc))
            {
                data.savedAtUtc = DateTime.UtcNow.ToString("o");
            }

            if (string.IsNullOrEmpty(data.displayName))
            {
                data.displayName = DefaultNameForSlot(data.slot);
            }

            try
            {
                File.WriteAllText(PathForSlot(data.slot), JsonUtility.ToJson(data, true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveSystem] 写入槽位 " + data.slot + " 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>删除一个槽位。</summary>
        public static bool DeleteSlot(int slot)
        {
            try
            {
                string path = PathForSlot(slot);
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SaveSystem] 删除槽位 " + slot + " 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把当前运行状态写进一个槽位。游戏侧以后要存更多东西，在这里扩字段即可。
        /// </summary>
        public static bool SaveCurrentState(int slot, Transform player, string progress = null)
        {
            if (player == null)
            {
                Debug.LogError("[SaveSystem] 没有玩家对象，无法保存可恢复的位置。");
                return false;
            }

            SaveSlotData data = new SaveSlotData
            {
                slot = slot,
                displayName = DefaultNameForSlot(slot),
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                sceneName = SceneManager.GetActiveScene().name,
                progress = progress ?? string.Empty
            };

            Vector3 p = player.position;
            data.posX = p.x;
            data.posY = p.y;
            data.posZ = p.z;

            return SaveSlot(data);
        }

        /// <summary>槽位的默认显示名：「记录一 / 记录二 / 记录三」…</summary>
        public static string DefaultNameForSlot(int slot)
        {
            string[] numerals = { "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (slot >= 0 && slot < numerals.Length)
            {
                return "记录" + numerals[slot];
            }

            return "记录" + (slot + 1);
        }

        /// <summary>触发读取事件。由界面在用户确认读取后调用。</summary>
        public static void NotifyLoaded(SaveSlotData data)
        {
            if (data != null && SlotLoaded != null)
            {
                SlotLoaded(data);
            }
        }

        /// <summary>Validate a saved record before changing scenes.</summary>
        public static bool CanRestore(SaveSlotData data, out string error)
        {
            if (data == null || !data.IsValid || string.IsNullOrEmpty(data.sceneName))
            {
                error = "存档缺少场景或保存时间，无法读取。";
                return false;
            }

            if (float.IsNaN(data.posX) || float.IsInfinity(data.posX) ||
                float.IsNaN(data.posY) || float.IsInfinity(data.posY) ||
                float.IsNaN(data.posZ) || float.IsInfinity(data.posZ))
            {
                error = "存档位置数据无效，无法读取。";
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(data.sceneName))
            {
                error = "存档场景“" + data.sceneName + "”不在构建列表中。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Load the saved scene, then move its player after the scene has loaded.</summary>
        public static bool Restore(SaveSlotData data, out string error)
        {
            if (!CanRestore(data, out error)) return false;

            SceneManager.sceneLoaded -= OnRestoreSceneLoaded;
            m_PendingRestore = data;
            SceneManager.sceneLoaded += OnRestoreSceneLoaded;
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;

            try
            {
                SceneManager.LoadScene(data.sceneName);
                return true;
            }
            catch (Exception e)
            {
                SceneManager.sceneLoaded -= OnRestoreSceneLoaded;
                m_PendingRestore = null;
                Time.timeScale = previousTimeScale;
                error = "加载存档场景失败：" + e.Message;
                return false;
            }
        }

        private static SaveSlotData m_PendingRestore;

        private static void OnRestoreSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_PendingRestore == null || scene.name != m_PendingRestore.sceneName) return;

            SceneManager.sceneLoaded -= OnRestoreSceneLoaded;
            SaveSlotData data = m_PendingRestore;
            m_PendingRestore = null;

            PlayerController player = UnityEngine.Object.FindObjectOfType<PlayerController>();
            if (player == null)
            {
                Debug.LogError("[SaveSystem] 场景“" + scene.name + "”没有 PlayerController，无法恢复玩家位置。");
                RestoreNotice.Show("已加载“" + scene.name + "”，但场景中没有玩家，位置无法恢复。");
                return;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.transform.position = new Vector3(data.posX, data.posY, data.posZ);
            if (controller != null) controller.enabled = true;
            NotifyLoaded(data);
        }
    }

    public sealed class RestoreNotice : MonoBehaviour
    {
        private string m_Message;
        private float m_ExpiresAt;

        public static void Show(string message)
        {
            GameObject go = new GameObject("读档提示");
            RestoreNotice notice = go.AddComponent<RestoreNotice>();
            notice.m_Message = message;
            notice.m_ExpiresAt = Time.unscaledTime + 8f;
        }

        private void Update()
        {
            if (Time.unscaledTime >= m_ExpiresAt) Destroy(gameObject);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(Screen.width * 0.2f, 24f, Screen.width * 0.6f, 64f), m_Message);
        }
    }
}
