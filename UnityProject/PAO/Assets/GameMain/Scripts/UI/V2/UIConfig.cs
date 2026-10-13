using System;
using UnityEngine;

namespace PAO.UI.V2
{
    [Serializable]
    public sealed class ChapterDefinition
    {
        public string scene;
        public string title;
        [TextArea] public string description;
        public string objective;
        [Tooltip("玩家缺失时，在此命名物体的上表面生成默认玩家。")]
        public string spawnAnchor;
        [Tooltip("已有终点时忽略；否则在此命名物体上创建互动终点。")]
        public string goalAnchor;
    }

    [Serializable]
    public sealed class EndingDefinition
    {
        public string id;
        public string title;
        [TextArea] public string description;
        [Min(0)] public int requiredMemories;
    }

    [CreateAssetMenu(menuName = "PAO/UI/UI Configuration", fileName = "PAOUIConfig")]
    public sealed class UIConfig : ScriptableObject
    {
        public bool enabledInBuild = true;
        public string menuScene = "Menu";
        public string[] additionalGameplayScenes = { "cs", "Control UI" };
        public Font font;
        public Sprite round;
        public Sprite ring;
        public Sprite menuArt;
        public Sprite[] abilityIcons;
        public GameObject defaultPlayer;
        public Color paper = new Color32(245, 240, 232, 255);
        public Color ink = new Color32(23, 56, 77, 255);
        public Color teal = new Color32(47, 111, 120, 255);
        public Color mist = new Color32(234, 243, 241, 255);
        public ChapterDefinition[] chapters = new ChapterDefinition[0];
        public EndingDefinition[] endings = new EndingDefinition[0];
        [TextArea] public string credits = "泡涌 · 意识之海\nTapTap Game Jam 21\n\n感谢每一位参与创作与体验的朋友。";

        public int IndexOf(string scene)
        {
            for (int i = 0; i < chapters.Length; i++)
                if (chapters[i].scene == scene) return i;
            return -1;
        }

        public bool Supports(string scene)
        {
            return scene == menuScene || IndexOf(scene) >= 0 ||
                   Array.IndexOf(additionalGameplayScenes, scene) >= 0;
        }
    }
}
