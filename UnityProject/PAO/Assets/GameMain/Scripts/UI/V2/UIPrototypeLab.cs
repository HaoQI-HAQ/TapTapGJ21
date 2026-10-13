using UnityEngine;

namespace PAO.UI.V2
{
    // The lab supplies explicitly labeled example content; production scenes use authored assets.
    public sealed class UIPrototypeLab : MonoBehaviour
    {
        public DialogueAsset dialogue;
        public BuildSite site;
        private GameObject examplePrefab;
        private void Start()
        {
            dialogue = ScriptableObject.CreateInstance<DialogueAsset>();
            dialogue.id = "ui-example-dialogue"; dialogue.title = "示例回声";
            dialogue.lines = new[] {
                new DialogueLine { speaker = "回声", text = "这是一段用于验证字幕与日志的示例内容。" },
                new DialogueLine { speaker = "回声", text = "你可以继续阅读，或返回后重新打开。" } };
            dialogue.choices = new[] { new DialogueChoice { text = "记住这段回声" }, new DialogueChoice { text = "让回声留在水中" } };
            examplePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            examplePrefab.name = "Example platform source"; examplePrefab.transform.localScale = new Vector3(3, 0.3f, 3);
            examplePrefab.SetActive(false);
            BuildRecipe recipe = ScriptableObject.CreateInstance<BuildRecipe>();
            recipe.id = "ui-example-platform"; recipe.title = "示例浮台";
            recipe.description = "验证预览、旋转、建构与拆除。"; recipe.prefab = examplePrefab; recipe.cost = 2;
            GameObject build = new GameObject("Example build site");
            build.transform.position = new Vector3(0, 1, 5);
            site = build.AddComponent<BuildSite>(); site.id = "ui-example-site"; site.recipes = new[] { recipe };
            if (UIRuntime.Instance != null) UIRuntime.Instance.Toast("UI 预览场景 · F2 打开全部模块入口", false);
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2) && UIRuntime.Instance != null)
                UIRuntime.Instance.ShowPrototypeCatalog(this);
        }
        private void OnDestroy()
        {
            if (dialogue != null) Destroy(dialogue);
            if (site != null && site.recipes.Length > 0) Destroy(site.recipes[0]);
            if (examplePrefab != null) Destroy(examplePrefab);
        }
    }
}
