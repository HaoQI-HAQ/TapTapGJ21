// 由《泡涌》剧本文案（总）.docx 精简生成：只保留第 1 关 M 主线，每段≤4 句。
// 2026-10-18 二次精简：只留「关键机制教学」5 段（破坏 / 恢复 / 消耗 / 蓄力 / 工具也是材料），
// 开场叙事（M-001～M-004）、出口提示（M-014）、结尾提问（M-016）以及 B/H/R/X 支线全部不播。
using System.Collections.Generic;

namespace PAO
{
    public static class Map0NarrationData
    {
        public sealed class Beat
        {
            public readonly string id;
            public readonly string title;
            public readonly string trigger;
            public readonly string[] lines;
            public Beat(string id, string title, string trigger, string[] lines)
            { this.id = id; this.title = title; this.trigger = trigger; this.lines = lines; }
        }

        public static readonly Beat[] All = new Beat[]
        {
            // —— 关键机制教学（只留这 5 段）——
            new Beat("M-005", "第一道墙", "看到粉墙，直到打破它（破坏教学）", new string[] { "看向前方。那是一面墙。", "严格说不是——是许多小泡泡挤在一起。", "但它挡住了你的路。", "接下来很简单：打破它。" }),
            new Beat("M-007", "消失并不永久", "首次看见已破坏的粉墙重新形成", new string[] { "等等。", "你打破的东西，正在重新长回来。", "一点一点，像一道缓慢愈合的伤口。", "可这里没有伤口——破裂与恢复，都是正常的。" }),
            new Beat("M-008", "代价与补充", "泡泡值首次不足，直到观察到回升（资源教学）", new string[] { "现在，谈一件不太愉快的事：消耗。", "每一次发射泡泡，都要花掉一点泡泡值。", "泡泡值不是无限的。", "但你看——泡泡值在回来。" }),
            new Beat("M-011", "蓄力", "解锁蓄力，直到首次用蓄满泡泡打穿墙", new string[] { "现在，教你一件事：按住不放。", "泡泡会慢慢变大。变大，意味着变强。", "但也要花掉更多。", "按住多久，是只有你能做的决定。" }),
            new Beat("M-013", "工具也是材料", "首次看见弹力泡泡落地转化为粉色材料", new string[] { "接下来这件事，可能有点难理解。", "你打出的泡泡，也能变成墙的一部分。", "工具，也是材料。", "你用来打破世界的东西，也能用来建造世界。" }),
        };

        private static Dictionary<string, Beat> s_Index;

        public static Beat Find(string id)
        {
            if (s_Index == null)
            {
                s_Index = new Dictionary<string, Beat>();
                for (int i = 0; i < All.Length; i++) s_Index[All[i].id] = All[i];
            }
            Beat beat;
            return s_Index.TryGetValue(id, out beat) ? beat : null;
        }
    }
}
