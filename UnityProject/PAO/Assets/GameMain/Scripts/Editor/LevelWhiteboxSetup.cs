using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// Builds geometry-only blockouts for Map0 through Map5. Re-running the command
    /// replaces only the generated root objects and leaves gameplay objects alone.
    /// </summary>
    public static class LevelWhiteboxSetup
    {
        private const string Map0Path = "Assets/GameMain/Scenes/Map0.unity";
        private const string Map1Path = "Assets/GameMain/Scenes/Map1.unity";
        private const string Map2Path = "Assets/GameMain/Scenes/Map2.unity";
        private const string Map3Path = "Assets/GameMain/Scenes/Map3.unity";
        private const string Map4Path = "Assets/GameMain/Scenes/Map4.unity";
        private const string Map5Path = "Assets/GameMain/Scenes/Map5.unity";
        private const string MaterialFolder = "Assets/GameMain/Art/WhiteboxMaterials";

        private static Material m_Ground;
        private static Material m_Edge;
        private static Material m_Void;
        private static Material m_Bounce;
        private static Material m_Sticky;
        private static Material m_Bomb;
        private static Material m_PinkWall;
        private static Material m_Landmark;

        [MenuItem("Tools/PAO/关卡白盒/搭建 Map0 + Map1", priority = 80)]
        public static void BuildBoth()
        {
            EnsureMaterials();
            BuildMap(Map0Path, BuildMap0Geometry);
            BuildMap(Map1Path, BuildMap1Geometry);

            Scene map0 = EditorSceneManager.OpenScene(Map0Path, OpenSceneMode.Single);
            GameObject root = GameObject.Find("Map0_Whitebox");
            if (root != null)
            {
                Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            EditorSceneManager.SetActiveScene(map0);
            Debug.Log("[PAO Whitebox] Map0 and Map1 blockout geometry saved. Map0 is open.");
        }

        [MenuItem("Tools/PAO/关卡白盒/搭建 Map2 - Map5", priority = 81)]
        public static void BuildMap2To5()
        {
            EnsureMaterials();
            BuildMap(Map2Path, BuildMap2Geometry);
            BuildMap(Map3Path, BuildMap3Geometry);
            BuildMap(Map4Path, BuildMap4Geometry);
            BuildMap(Map5Path, BuildMap5Geometry);

            Scene map2 = EditorSceneManager.OpenScene(Map2Path, OpenSceneMode.Single);
            GameObject root = GameObject.Find("Map2_Whitebox");
            if (root != null)
            {
                Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            EditorSceneManager.SetActiveScene(map2);
            Debug.Log("[PAO Whitebox] Map2 through Map5 blockout geometry saved. Map2 is open.");
        }

        private static void BuildMap(string scenePath, System.Action<Transform> build)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string rootName = System.IO.Path.GetFileNameWithoutExtension(scenePath) + "_Whitebox";
            GameObject previous = GameObject.Find(rootName);
            if (previous != null)
                Object.DestroyImmediate(previous);

            var root = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            build(root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void BuildMap0Geometry(Transform root)
        {
            // Open, uncluttered awakening space. The floor is split around a real pit.
            Box(root, "Ground_FrontOpenSpace", new Vector3(0, -0.25f, -4.75f), new Vector3(30, 0.5f, 13.5f), m_Ground);
            Box(root, "Ground_LeftOfPit", new Vector3(-7.1f, -0.25f, 6.1f), new Vector3(15.8f, 0.5f, 8.8f), m_Ground);
            Box(root, "Ground_RightOfPit", new Vector3(7.1f, -0.25f, 6.1f), new Vector3(15.8f, 0.5f, 8.8f), m_Ground);
            Box(root, "Ground_FarBeyondPit", new Vector3(0, -0.25f, 12.0f), new Vector3(30, 0.5f, 3.0f), m_Ground);

            // Visual-only depth backing below the opening; no collider, so it remains a void.
            Box(root, "Void_Backdrop_NoCollider", new Vector3(0, -5.5f, 7.2f), new Vector3(8.0f, 0.15f, 7.2f), m_Void, false);
            // Low, broken rim pieces make the edge readable without fencing the open scene.
            Box(root, "PitRim_Left", new Vector3(-4.25f, 0.08f, 7.0f), new Vector3(0.35f, 0.22f, 7.0f), m_Edge);
            Box(root, "PitRim_Right", new Vector3(4.25f, 0.08f, 7.0f), new Vector3(0.35f, 0.22f, 7.0f), m_Edge);
            Box(root, "PitRim_FrontLeft", new Vector3(-2.7f, 0.08f, 3.45f), new Vector3(3.0f, 0.22f, 0.3f), m_Edge);
            Box(root, "PitRim_FrontRight", new Vector3(2.7f, 0.08f, 3.45f), new Vector3(3.0f, 0.22f, 0.3f), m_Edge);

            // Large destructible-bubble stand-in blocks the opening; it is geometry only.
            Sphere(root, "Blocker_LargeDestructibleBubble", new Vector3(0, 2.1f, 5.5f), 4.4f, m_PinkWall);

            // Small optional interaction stand-ins, off the direct approach.
            Sphere(root, "Optional_BounceBubble_Left", new Vector3(-8.0f, 1.0f, -0.5f), 1.25f, m_Bounce);
            Sphere(root, "Optional_BounceBubble_Right", new Vector3(8.0f, 1.0f, -0.2f), 1.25f, m_Bounce);

            // Quiet distant landmark, built from plain cubes only.
            Box(root, "FarLandmark_Base", new Vector3(0, 0.25f, 16.2f), new Vector3(7.0f, 0.5f, 2.0f), m_Landmark);
            Box(root, "FarLandmark_Tower", new Vector3(0, 2.5f, 16.2f), new Vector3(1.5f, 4.0f, 1.5f), m_Landmark);
            Box(root, "FarLandmark_LeftSpire", new Vector3(-2.0f, 1.7f, 16.2f), new Vector3(0.65f, 2.4f, 0.65f), m_Landmark);
            Box(root, "FarLandmark_RightSpire", new Vector3(2.0f, 1.7f, 16.2f), new Vector3(0.65f, 2.4f, 0.65f), m_Landmark);
        }

        private static void BuildMap1Geometry(Transform root)
        {
            // Starting island and broad, forgiving landing island.
            Box(root, "Island_Start", new Vector3(-8.0f, -0.25f, 0.0f), new Vector3(11.0f, 0.5f, 12.0f), m_Ground);
            Box(root, "Island_Landing", new Vector3(8.0f, 0.15f, 0.0f), new Vector3(10.0f, 0.8f, 11.0f), m_Ground);
            Box(root, "Abyss_Backdrop_NoCollider", new Vector3(0, -5.0f, 0), new Vector3(10.0f, 0.15f, 18.0f), m_Void, false);

            // Main stepping route across the gap, with changing elevation and generous pads.
            Box(root, "Step_01_Low", new Vector3(-2.6f, 0.05f, 2.7f), new Vector3(2.8f, 0.5f, 3.0f), m_Edge);
            Box(root, "Step_02_Mid", new Vector3(0.1f, 0.85f, 0.8f), new Vector3(2.8f, 0.5f, 2.8f), m_Edge);
            Box(root, "Step_03_High", new Vector3(2.5f, 1.55f, -1.2f), new Vector3(2.8f, 0.5f, 2.8f), m_Edge);
            Box(root, "Step_04_Landing", new Vector3(5.0f, 0.9f, 1.8f), new Vector3(2.8f, 0.5f, 3.0f), m_Edge);

            // Lower, longer side route gives an alternate path and room to experiment.
            Box(root, "SideRoute_01", new Vector3(-2.4f, -0.05f, 5.8f), new Vector3(2.4f, 0.45f, 2.2f), m_Landmark);
            Box(root, "SideRoute_02", new Vector3(0.2f, -0.05f, 6.2f), new Vector3(2.4f, 0.45f, 2.2f), m_Landmark);
            Box(root, "SideRoute_03", new Vector3(2.8f, 0.05f, 5.8f), new Vector3(2.4f, 0.45f, 2.2f), m_Landmark);

            // Floating-bubble stand-ins suggest several possible connection shapes.
            Sphere(root, "AnchorBubble_01", new Vector3(-4.7f, 1.4f, 1.6f), 0.95f, m_Sticky);
            Sphere(root, "AnchorBubble_02", new Vector3(-2.3f, 2.0f, 0.4f), 0.95f, m_Sticky);
            Sphere(root, "AnchorBubble_03", new Vector3(0.0f, 2.6f, -0.7f), 0.95f, m_Sticky);
            Sphere(root, "AnchorBubble_04", new Vector3(2.4f, 2.7f, -0.1f), 0.95f, m_Sticky);
            Sphere(root, "AnchorBubble_05", new Vector3(4.4f, 2.2f, 1.0f), 0.95f, m_Sticky);

            // A few stable visual landmarks help read elevation and the destination.
            Box(root, "LandingLandmark_Plinth", new Vector3(8.0f, 0.7f, -3.7f), new Vector3(2.2f, 0.7f, 1.4f), m_Landmark);
            Box(root, "Boundary_LeftGuide", new Vector3(-13.3f, 0.5f, 0.0f), new Vector3(0.35f, 1.0f, 12.0f), m_Edge);
            Box(root, "Boundary_RightGuide", new Vector3(13.3f, 0.5f, 0.0f), new Vector3(0.35f, 1.0f, 12.0f), m_Edge);
        }

        // Map2: the floating lift is a short vertical window. Three side exits are
        // readable from the floor; the lower one is safe but longer, the upper one
        // reaches the goal fastest. Geometry only; timings remain gameplay tuning.
        private static void BuildMap2Geometry(Transform root)
        {
            Box(root, "Start_Shelf", new Vector3(-10, 0, 0), new Vector3(7, 0.6f, 9), m_Ground);
            Box(root, "Lift_Base", new Vector3(-3.7f, 0.2f, 0), new Vector3(3.2f, 0.6f, 3.4f), m_Edge);
            Box(root, "Mid_Checkpoint", new Vector3(3.5f, 4.2f, 0), new Vector3(4.4f, 0.7f, 5), m_Landmark);
            Box(root, "Top_Goal_Platform", new Vector3(10, 8.5f, -2.8f), new Vector3(5, 0.7f, 4), m_Bounce);
            Box(root, "Upper_Exit_Lip", new Vector3(6.3f, 7.3f, -1.2f), new Vector3(1.4f, 0.35f, 1.3f), m_Edge);
            Box(root, "Mid_Exit_Lip", new Vector3(0.2f, 3.1f, 2.8f), new Vector3(1.5f, 0.35f, 1.4f), m_Edge);
            Box(root, "Low_Recovery_Route_A", new Vector3(-0.4f, -2.2f, 5), new Vector3(3.4f, 0.5f, 2), m_Edge);
            Box(root, "Low_Recovery_Route_B", new Vector3(4, -2.0f, 6.2f), new Vector3(3.2f, 0.5f, 2), m_Edge);
            Box(root, "Low_Recovery_Goal", new Vector3(10, -1.4f, 5), new Vector3(4.5f, 0.7f, 3.5f), m_Landmark);
            Sphere(root, "Lift_Bubble_Visual", new Vector3(-3.7f, 2.2f, 0), 1.8f, m_Sticky);
            Box(root, "Sightline_Column_Left", new Vector3(2.4f, 2.3f, -3.5f), new Vector3(0.5f, 4.6f, 0.5f), m_Void);
            Box(root, "Sightline_Column_Right", new Vector3(2.4f, 2.3f, 3.5f), new Vector3(0.5f, 4.6f, 0.5f), m_Void);
            Box(root, "Abyss_Backdrop_NoCollider", new Vector3(0, -7, 0), new Vector3(30, 0.15f, 22), m_Void, false);
        }

        // Map3: rising bubble ride, timed lateral transfer, then a bounce launch
        // across a split gate. Goal is the highest small shelf, with a lower reset.
        private static void BuildMap3Geometry(Transform root)
        {
            Box(root, "Start_Island", new Vector3(-12, 0, 1), new Vector3(7, 0.7f, 8), m_Ground);
            Box(root, "SetUp_Perch", new Vector3(-6.5f, 1.7f, 1), new Vector3(2.4f, 0.45f, 2.2f), m_Edge);
            Box(root, "Lift_Transfer_Shelf", new Vector3(-1.2f, 5.7f, -2.6f), new Vector3(3.2f, 0.55f, 2.8f), m_Landmark);
            Box(root, "Needle_Platform_A", new Vector3(2.2f, 7.0f, -0.3f), new Vector3(1.8f, 0.45f, 1.8f), m_Edge);
            Box(root, "Needle_Platform_B", new Vector3(5.8f, 8.0f, 1.8f), new Vector3(1.8f, 0.45f, 1.8f), m_Edge);
            Box(root, "High_Goal", new Vector3(10.2f, 9.1f, -0.1f), new Vector3(3.4f, 0.65f, 3.2f), m_Bounce);
            Box(root, "Lower_Safe_Route_01", new Vector3(-3, -1.7f, 5), new Vector3(3, 0.45f, 2), m_Edge);
            Box(root, "Lower_Safe_Route_02", new Vector3(1.3f, -1.2f, 6.5f), new Vector3(3, 0.45f, 2), m_Edge);
            Box(root, "Lower_Safe_Route_03", new Vector3(6, -0.7f, 5.2f), new Vector3(3, 0.45f, 2), m_Edge);
            Box(root, "Lower_Recovery_Perch", new Vector3(10.2f, -0.2f, 5.2f), new Vector3(3.5f, 0.65f, 3), m_Landmark);
            Sphere(root, "Lift_Bubble_01", new Vector3(-4.8f, 2.5f, 0.8f), 1.7f, m_Sticky);
            Sphere(root, "Bounce_Bubble_02", new Vector3(2.2f, 5.3f, -0.3f), 1.2f, m_Bounce);
            Box(root, "Gate_Left_Pillar", new Vector3(7.9f, 7.8f, -1.4f), new Vector3(0.45f, 2.8f, 0.45f), m_PinkWall);
            Box(root, "Gate_Right_Pillar", new Vector3(7.9f, 7.8f, 1.4f), new Vector3(0.45f, 2.8f, 0.45f), m_PinkWall);
            Box(root, "Abyss_Backdrop_NoCollider", new Vector3(0, -6, 0), new Vector3(32, 0.15f, 24), m_Void, false);
        }

        // Map4: a vertical shaft with a breakable membrane at the top. The player
        // can take a narrow mid-level transfer, but the intended goal is the lowest
        // point: descend through staggered openings during the limited float window.
        private static void BuildMap4Geometry(Transform root)
        {
            Box(root, "Top_Entry_Deck", new Vector3(0, 10, -9), new Vector3(12, 0.7f, 5), m_Ground);
            Box(root, "Upper_Lip_Left", new Vector3(-5.1f, 9.4f, -4.8f), new Vector3(1.3f, 0.5f, 4), m_Edge);
            Box(root, "Upper_Lip_Right", new Vector3(5.1f, 9.4f, -4.8f), new Vector3(1.3f, 0.5f, 4), m_Edge);
            Box(root, "Mid_Ledge_A", new Vector3(3.5f, 5.4f, 0), new Vector3(4, 0.5f, 3), m_Edge);
            Box(root, "Mid_Ledge_B", new Vector3(-3.6f, 2.2f, 3.2f), new Vector3(4, 0.5f, 3), m_Landmark);
            Box(root, "Low_Ledge_C", new Vector3(3.5f, -1.8f, 6.1f), new Vector3(4, 0.5f, 3), m_Edge);
            Box(root, "Low_Ledge_D", new Vector3(-3.5f, -5.2f, 8.2f), new Vector3(4, 0.5f, 3), m_Edge);
            Box(root, "Bottom_Goal", new Vector3(0, -9.0f, 11), new Vector3(6, 0.7f, 5), m_Bounce);
            Box(root, "Mid_Optional_Exit", new Vector3(9, 3, 2), new Vector3(3.5f, 0.7f, 3), m_Landmark);
            Sphere(root, "Breakable_Bubble_Membrane", new Vector3(0, 8.6f, -3.0f), 3.3f, m_PinkWall);
            Box(root, "Shaft_Backdrop_NoCollider", new Vector3(0, 0, 1), new Vector3(15, 0.15f, 22), m_Void, false);
            Box(root, "Shaft_Wall_Left", new Vector3(-7.4f, 0, 1), new Vector3(0.45f, 21, 22), m_Void);
            Box(root, "Shaft_Wall_Right", new Vector3(7.4f, 0, 1), new Vector3(0.45f, 21, 22), m_Void);
            Box(root, "Timing_Arch_01", new Vector3(0, 5.7f, -1.8f), new Vector3(5.5f, 0.3f, 0.3f), m_Landmark);
            Box(root, "Timing_Arch_02", new Vector3(0, -2.4f, 5.1f), new Vector3(5.5f, 0.3f, 0.3f), m_Landmark);
        }

        // Map5: multi-phase route. A destructible plug exposes an ascent shaft;
        // a timed exit leads to a narrow traverse, then a second lift sends the
        // player to a goal placed midway/high, with a low route as a deliberate
        // bailout. More beats and tighter lateral choices than Map2-4.
        private static void BuildMap5Geometry(Transform root)
        {
            Box(root, "Start_Courtyard", new Vector3(-14, 0, 0), new Vector3(8, 0.7f, 10), m_Ground);
            Box(root, "Plug_Approach", new Vector3(-8.4f, 0.3f, 0), new Vector3(3.2f, 0.55f, 3.2f), m_Edge);
            Sphere(root, "Breakable_Plug_Visual", new Vector3(-4.7f, 2.0f, 0), 3.0f, m_PinkWall);
            Box(root, "Shaft_Floor_01", new Vector3(-1, -2.2f, 0), new Vector3(3, 0.55f, 3), m_Edge);
            Box(root, "Shaft_Exit_Upper", new Vector3(2.8f, 4.6f, -2.8f), new Vector3(3, 0.55f, 2.6f), m_Landmark);
            Box(root, "Side_Transfer_01", new Vector3(6.2f, 3.4f, -0.3f), new Vector3(2.4f, 0.45f, 1.8f), m_Edge);
            Box(root, "Side_Transfer_02", new Vector3(9.3f, 2.2f, 2), new Vector3(2.4f, 0.45f, 1.8f), m_Edge);
            Box(root, "Mid_Checkpoint", new Vector3(12.8f, 1.3f, -0.4f), new Vector3(4, 0.65f, 3.5f), m_Ground);
            Box(root, "Second_Lift_Perch", new Vector3(16.7f, 2.3f, -2.4f), new Vector3(2.4f, 0.45f, 2), m_Edge);
            Box(root, "Final_Upper_Goal", new Vector3(21, 7.2f, -4.5f), new Vector3(4, 0.7f, 3.4f), m_Bounce);
            Box(root, "Mid_Lateral_Goal", new Vector3(21.3f, 1.0f, 3.8f), new Vector3(4, 0.7f, 3.4f), m_Landmark);
            Box(root, "Low_Bailout_01", new Vector3(5.5f, -3.7f, 5.4f), new Vector3(3.1f, 0.5f, 2), m_Edge);
            Box(root, "Low_Bailout_02", new Vector3(10.2f, -4.0f, 6.6f), new Vector3(3.1f, 0.5f, 2), m_Edge);
            Box(root, "Low_Bailout_03", new Vector3(15.2f, -3.2f, 5.4f), new Vector3(3.1f, 0.5f, 2), m_Edge);
            Sphere(root, "Lift_Bubble_A", new Vector3(0.5f, 1.3f, 0), 1.7f, m_Sticky);
            Sphere(root, "Lift_Bubble_B", new Vector3(15.4f, 4.0f, -2.7f), 1.5f, m_Sticky);
            Box(root, "Overhead_Obstacle_Left", new Vector3(6.0f, 6.5f, -2.3f), new Vector3(4.6f, 0.45f, 0.45f), m_Void);
            Box(root, "Overhead_Obstacle_Right", new Vector3(10.1f, 5.1f, 1.8f), new Vector3(0.45f, 4.5f, 0.45f), m_Void);
            Box(root, "Abyss_Backdrop_NoCollider", new Vector3(3.5f, -8, 0), new Vector3(42, 0.15f, 28), m_Void, false);
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 position, float diameter, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/GameMain/Art", "WhiteboxMaterials");

            m_Ground = GetMaterial("WB_Ground", new Color(0.72f, 0.76f, 0.75f));
            m_Edge = GetMaterial("WB_Platform", new Color(0.55f, 0.65f, 0.66f));
            m_Void = GetMaterial("WB_Void", new Color(0.055f, 0.13f, 0.18f));
            m_Bounce = GetMaterial("WB_Bounce", new Color(0.20f, 0.63f, 0.88f));
            m_Sticky = GetMaterial("WB_FloatSticky", new Color(0.20f, 0.76f, 0.67f));
            m_Bomb = GetMaterial("WB_Bomb", new Color(0.96f, 0.43f, 0.20f));
            m_PinkWall = GetMaterial("WB_Destructible", new Color(0.88f, 0.42f, 0.63f));
            m_Landmark = GetMaterial("WB_Landmark", new Color(0.77f, 0.83f, 0.80f));
            AssetDatabase.SaveAssets();
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
