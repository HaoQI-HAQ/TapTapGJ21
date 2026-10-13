using System;
using UnityEngine;
using UnityEngine.Events;

namespace PAO.UI.V2
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BuildSite : MonoBehaviour
    {
        public string id;
        public Transform placement;
        public BuildRecipe[] recipes = new BuildRecipe[0];
        public bool validPlacement = true;
        [TextArea] public string invalidReason = "这里的水流过于湍急，无法放置。";
        public UnityEvent placed;
        private bool nearby;
        private GameObject instance;
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (placement == null) placement = transform;
            if (string.IsNullOrEmpty(id)) Debug.LogWarning("[UI] BuildSite 需要唯一且稳定的 id，才能恢复建构。", this);
        }
        private void Start()
        {
            BuiltItem record = UIRunState.Progress.builds.Find(b => b.site == id && b.scene == gameObject.scene.name);
            if (record == null) return;
            BuildRecipe recipe = Array.Find(recipes, r => r != null && r.id == record.recipe);
            if (recipe != null && recipe.prefab != null) Spawn(recipe, record.rotation);
        }
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            nearby = true;
            if (UIRuntime.Instance != null) UIRuntime.Instance.Toast(UISettings.Text("查看建构 · ", "Inspect construction · ") + UISettings.Key(3, KeyCode.E), false);
        }
        private void OnTriggerExit(Collider other)
        { if (other.GetComponentInParent<PlayerController>() != null) nearby = false; }
        private void Update()
        {
            if (nearby && Input.GetKeyDown(UISettings.Key(3, KeyCode.E)) && UIRuntime.Instance != null && !UIRuntime.Instance.IsBlockingGameplay)
                UIRuntime.Instance.ShowConstruction(this);
        }
        public string CanPlace(BuildRecipe recipe)
        {
            if (instance != null) return UISettings.Text("这里已经完成建构。", "This site is already occupied.");
            if (string.IsNullOrEmpty(id)) return UISettings.Text("这个建构点尚未准备好。", "This construction site is not ready.");
            if (recipe == null || recipe.prefab == null || string.IsNullOrEmpty(recipe.id)) return UISettings.Text("这个配方尚未准备好。", "This recipe is not ready.");
            if (!validPlacement) return invalidReason;
            if (UIRunState.Progress.buildTokens < recipe.cost) return UISettings.Text("意识能量不足。", "Not enough consciousness energy.");
            return null;
        }
        public bool Place(BuildRecipe recipe, float rotation)
        {
            if (CanPlace(recipe) != null) return false;
            Spawn(recipe, rotation);
            UIRunState.Progress.buildTokens -= recipe.cost;
            UIRunState.Progress.builds.Add(new BuiltItem { site = id, scene = gameObject.scene.name, recipe = recipe.id, rotation = rotation, cost = recipe.cost });
            UIRunState.Notify(); if (placed != null) placed.Invoke(); return true;
        }
        private void Spawn(BuildRecipe recipe, float rotation)
        {
            instance = Instantiate(recipe.prefab, placement.position, placement.rotation * Quaternion.Euler(0, rotation, 0));
            instance.SetActive(true);
        }
        public bool Remove()
        {
            BuiltItem record = UIRunState.Progress.builds.Find(b => b.site == id && b.scene == gameObject.scene.name);
            if (record == null) return false;
            UIRunState.Progress.buildTokens += record.cost; UIRunState.Progress.builds.Remove(record);
            if (instance != null) Destroy(instance); instance = null; UIRunState.Notify(); return true;
        }
    }

    [Serializable]
    public sealed class BuiltItem
    {
        public string scene;
        public string site;
        public string recipe;
        public float rotation;
        public int cost;
    }
}
