using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>
    /// 小工具：把当前选中的物体（含包围盒、父级路径、组件）导成 JSON 写到 Temp/selection.json。
    /// 只是给"离线核对坐标"用的，不影响场景。
    ///
    /// 菜单：Tools/PAO/调试/导出当前选中到 JSON
    /// </summary>
    public static class SelectionDump
    {
        [MenuItem("Tools/PAO/调试/导出当前选中到 JSON", priority = 95)]
        public static void Dump()
        {
            GameObject[] selected = Selection.gameObjects;
            StringBuilder sb = new StringBuilder(4096);

            sb.Append("{\"selected\":[");

            for (int i = 0; i < selected.Length; i++)
            {
                if (i > 0) sb.Append(',');

                GameObject go = selected[i];

                if (go == null)
                {
                    sb.Append("null");
                    continue;
                }

                sb.Append('{');
                sb.Append("\"name\":\"").Append(Esc(go.name)).Append("\",");
                sb.Append("\"path\":\"").Append(Esc(PathOf(go.transform))).Append("\",");
                sb.Append("\"pos\":").Append(Vec(go.transform.position)).Append(',');
                sb.Append("\"euler\":").Append(Vec(go.transform.eulerAngles)).Append(',');
                sb.Append("\"scale\":").Append(Vec(go.transform.lossyScale)).Append(',');

                Bounds bounds;
                bool has = TryGetBounds(go.transform, out bounds);

                sb.Append("\"bounds\":");
                sb.Append(has ? "{\"center\":" + Vec(bounds.center) + ",\"size\":" + Vec(bounds.size) + "}"
                              : "null");
                sb.Append(',');

                sb.Append("\"components\":[");

                Component[] comps = go.GetComponents<Component>();
                bool first = true;

                for (int c = 0; c < comps.Length; c++)
                {
                    if (comps[c] == null) continue;

                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Esc(comps[c].GetType().Name)).Append('"');
                }

                sb.Append("]}");
            }

            sb.Append("]}");

            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/selection.json");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[SelectionDump] 写出 " + path + "（选中 " + selected.Length + " 个）");
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            if (has) return true;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;

                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }

            return has;
        }

        private static string PathOf(Transform t)
        {
            StringBuilder sb = new StringBuilder(t.name);
            Transform p = t.parent;

            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }

            return sb.ToString();
        }

        private static string Vec(Vector3 v)
        {
            return "[" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + "]";
        }

        private static string Esc(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
