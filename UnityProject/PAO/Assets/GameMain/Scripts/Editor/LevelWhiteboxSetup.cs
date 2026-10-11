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
            // Maps 2–5 are open underwater regions, deliberately twice the
            // previous footprint so the player can read and choose between
            // distant caverns, shelves and vertical routes.
            if (scenePath == Map2Path || scenePath == Map3Path || scenePath == Map4Path || scenePath == Map5Path)
                root.transform.localScale = Vector3.one * 2f;
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

        // Map2 / level 3: broad drowned harbor district for the explosion tutorial.
        // The player can approach the memory beacon by the open quay, through a
        // breakable arcade, or via the lower cistern. Large props suggest blast,
        // mass and inertia experiments; nothing encodes one required route.
        private static void BuildMap2Geometry(Transform root)
        {
            Box(root, "Harbor_West_Quay", new Vector3(-14, -0.5f, 1), new Vector3(19, 1, 24), m_Ground);
            Box(root, "Harbor_Central_Piazza", new Vector3(1, -0.35f, 1), new Vector3(19, 1, 21), m_Ground);
            Box(root, "Harbor_East_Terrace", new Vector3(15, 1.0f, 1), new Vector3(14, 1, 22), m_Ground);
            Box(root, "Harbor_North_Calcite_Shelf", new Vector3(0, 2.1f, 16), new Vector3(28, 1, 12), m_Ground);
            Box(root, "Harbor_South_Cistern_Floor", new Vector3(0, -3.2f, -15), new Vector3(28, 1, 11), m_Edge);
            Box(root, "Cistern_Entry_Step_01", new Vector3(-3, -1.2f, -7), new Vector3(8, 0.7f, 3), m_Edge);
            Box(root, "Cistern_Entry_Step_02", new Vector3(-1, -2.3f, -10), new Vector3(9, 0.7f, 3), m_Edge);
            Box(root, "Harbor_Void_Backdrop_NoCollider", new Vector3(0, -8, 1), new Vector3(50, 0.15f, 42), m_Void, false);

            // Start district landmarks and loose physical props.
            Box(root, "Start_Salvage_Court", new Vector3(-19, 0.15f, -3), new Vector3(8, 0.35f, 7), m_Landmark);
            RuinArch(root, "West_Quay_Arch", new Vector3(-14, 0, 8), 5.5f, 4.5f, m_Edge);
            Box(root, "Blast_Test_Block_Large", new Vector3(-8, 0.8f, -4), new Vector3(2.4f, 1.6f, 2.2f), m_Bomb);
            Box(root, "Blast_Test_Block_Small", new Vector3(-5.2f, 0.55f, -4.2f), new Vector3(1.5f, 1.1f, 1.5f), m_Bounce);
            Box(root, "Mooring_Bollard_A", new Vector3(-20, 0.65f, 5), new Vector3(0.8f, 1.3f, 0.8f), m_Landmark);
            Box(root, "Mooring_Bollard_B", new Vector3(-18, 0.65f, 5), new Vector3(0.8f, 1.3f, 0.8f), m_Landmark);

            // A small navigable block of ordinary architecture. The low street,
            // rooftop ledges and pink wall openings all reconnect to the piazza.
            RuinRoom(root, "West_Cracked_Grotto_A", new Vector3(-18, 0, 11), new Vector3(7, 5, 6), m_Edge, true);
            RuinRoom(root, "West_Cracked_Grotto_B", new Vector3(-9, 0, 13), new Vector3(6, 4, 6), m_Edge, false);
            Box(root, "West_Coral_Ridge_Walk", new Vector3(-13.5f, 2.7f, 13.5f), new Vector3(8, 0.45f, 2.4f), m_Landmark);
            Box(root, "West_Alley_Breakable_Panel", new Vector3(-5.25f, 1.25f, 10), new Vector3(0.45f, 2.5f, 3), m_PinkWall);
            Box(root, "West_Alley_Sill", new Vector3(-5.2f, 0.2f, 10), new Vector3(1.2f, 0.4f, 3.2f), m_Edge);
            Box(root, "West_Upper_Bypass", new Vector3(-4.3f, 2.9f, 13), new Vector3(4.4f, 0.45f, 2.2f), m_Landmark);

            // Central plaza is broad and open; broken columns frame the route but
            // do not funnel the player. A low ring permits exploration around it.
            Box(root, "Piazza_Memory_Fountain_Base", new Vector3(1, 0.25f, 2), new Vector3(5, 0.5f, 4), m_Landmark);
            Sphere(root, "Piazza_Fountain_Bubble", new Vector3(1, 1.5f, 2), 1.5f, m_Sticky);
            Box(root, "Piazza_Column_01", new Vector3(-5, 1.4f, -2), new Vector3(0.75f, 2.8f, 0.75f), m_Edge);
            Box(root, "Piazza_Column_02", new Vector3(7, 1.1f, -2), new Vector3(0.75f, 2.2f, 0.75f), m_Edge);
            Box(root, "Piazza_Column_03", new Vector3(-5, 1.6f, 7), new Vector3(0.75f, 3.2f, 0.75f), m_Edge);
            Box(root, "Piazza_Column_04", new Vector3(7, 1.3f, 7), new Vector3(0.75f, 2.6f, 0.75f), m_Edge);
            Box(root, "Piazza_Breakable_Arcade_Wall", new Vector3(9.5f, 1.3f, 3), new Vector3(0.45f, 2.6f, 6.4f), m_PinkWall);
            Box(root, "Piazza_Arcade_Post_A", new Vector3(9.5f, 1.3f, -1.1f), new Vector3(0.65f, 2.6f, 0.65f), m_Landmark);
            Box(root, "Piazza_Arcade_Post_B", new Vector3(9.5f, 1.3f, 7.1f), new Vector3(0.65f, 2.6f, 0.65f), m_Landmark);

            // East memory archive: climbable broad terraces, optional tower goal
            // and one intact side passage around the destructible facade.
            RuinRoom(root, "East_Memory_Grotto", new Vector3(15, 2, 7), new Vector3(9, 6, 8), m_Landmark, true);
            Box(root, "East_Grotto_Coral_Ledge", new Vector3(15, 5.2f, 7), new Vector3(9.5f, 0.5f, 8.5f), m_Edge);
            Box(root, "East_Grotto_Pink_Crust", new Vector3(10.25f, 3, 7), new Vector3(0.45f, 2.8f, 4.2f), m_PinkWall);
            Box(root, "East_Grotto_Side_Cleft", new Vector3(20, 2, 8), new Vector3(3.4f, 0.6f, 10), m_Edge);
            Box(root, "Memory_Beacon_Lower_Platform", new Vector3(17, 2, 16), new Vector3(5.5f, 0.55f, 4), m_Bounce);
            Box(root, "Memory_Beacon_Tower", new Vector3(17, 5, 16), new Vector3(1.6f, 5.5f, 1.6f), m_Landmark);
            Box(root, "Memory_Beacon_Cap", new Vector3(17, 8, 16), new Vector3(3.2f, 0.5f, 3.2f), m_Sticky);

            // Lower cistern offers a concealed loop back into the eastern district.
            Box(root, "Cistern_Wall_Left", new Vector3(-13, -0.4f, -15), new Vector3(0.55f, 5.6f, 11), m_Edge);
            Box(root, "Cistern_Wall_Right", new Vector3(13, -0.4f, -15), new Vector3(0.55f, 5.6f, 11), m_Edge);
            Box(root, "Cistern_Breakable_Grate", new Vector3(10, -0.2f, -11), new Vector3(0.45f, 2.8f, 4.5f), m_PinkWall);
            Box(root, "Cistern_Side_Exit", new Vector3(11, -2.4f, -7.5f), new Vector3(5.8f, 0.6f, 3), m_Landmark);
            Sphere(root, "Cistern_Anchor_Bubble_A", new Vector3(-7, -0.2f, -13), 1.2f, m_Sticky);
            Sphere(root, "Cistern_Anchor_Bubble_B", new Vector3(-3, 0.6f, -12), 1.2f, m_Sticky);
            Sphere(root, "Cistern_Anchor_Bubble_C", new Vector3(1, 1.5f, -11), 1.2f, m_Sticky);

            // A few elastic surfaces and the environmental hazard are landmarks,
            // not mandatory switches. No gameplay components are attached.
            Box(root, "Quay_Elastic_Wall", new Vector3(-21, 1.2f, 1), new Vector3(0.5f, 2.4f, 5), m_Bounce);
            Sphere(root, "Harbor_Explosive_Bubble_Visual", new Vector3(5, 1.2f, -10), 1.5f, m_Bomb);
            Box(root, "Outer_Cliff_Back", new Vector3(0, 1.2f, 23), new Vector3(48, 3, 0.8f), m_Void);
            Box(root, "Outer_Cliff_Left", new Vector3(-24, 1.2f, 0), new Vector3(0.8f, 3, 45), m_Void);
            Box(root, "Outer_Cliff_Right", new Vector3(24, 1.2f, 0), new Vector3(0.8f, 3, 45), m_Void);

            // Three extra vertical bands turn the harbor into a real undersea
            // sinkhole: the upper shelf is a long route, not a terminal goal;
            // the low crack hides a second way into the archive.
            Box(root, "Upper_Ceiling_Reef_Ledge_A", new Vector3(-14, 11, 11), new Vector3(12, 0.8f, 8), m_Edge);
            Box(root, "Upper_Ceiling_Reef_Ledge_B", new Vector3(0, 14, 17), new Vector3(14, 0.8f, 7), m_Landmark);
            Box(root, "Upper_Ceiling_Reef_Ledge_C", new Vector3(15, 11, 10), new Vector3(11, 0.8f, 8), m_Edge);
            Box(root, "Upper_Ledge_Broken_Span", new Vector3(1, 12.7f, 12), new Vector3(10, 0.6f, 1.4f), m_Bomb);
            CoralColony(root, "Upper_Coral_Canopy", new Vector3(1, 14, 20), 4.3f, m_Bounce);
            Box(root, "Lower_Crack_Entry", new Vector3(-2, -7, -19), new Vector3(11, 0.7f, 5), m_Edge);
            Box(root, "Lower_Crack_Mid_Shelf", new Vector3(8, -10, -24), new Vector3(9, 0.7f, 4), m_Landmark);
            Box(root, "Lower_Crack_Exit_Shelf", new Vector3(15, -7, -18), new Vector3(10, 0.7f, 4), m_Edge);
            RockSpire(root, "Siphon_Vent_Pillar_A", new Vector3(4, -4, -17), 7, 2, -8, m_Bounce);
            RockSpire(root, "Siphon_Vent_Pillar_B", new Vector3(9, 1, -18), 8, 1.7f, 13, m_Sticky);
            Box(root, "Fulcrum_Puzzle_Keel", new Vector3(-2, 1.2f, 5), new Vector3(11, 0.7f, 1.5f), m_Bomb);
            Box(root, "Fulcrum_Puzzle_Pink_Crust_A", new Vector3(-6, 0.9f, 4), new Vector3(0.5f, 1.8f, 2), m_PinkWall);
            Box(root, "Fulcrum_Puzzle_Pink_Crust_B", new Vector3(2, 0.9f, 4), new Vector3(0.5f, 1.8f, 2), m_PinkWall);
            Box(root, "Fulcrum_Puzzle_Weight_A", new Vector3(-4, 2.1f, 5), new Vector3(1.8f, 1.8f, 1.8f), m_Edge);
            Box(root, "Fulcrum_Puzzle_Weight_B", new Vector3(4, 2.1f, 5), new Vector3(1.8f, 1.8f, 1.8f), m_Landmark);

            ExploratoryNook(root, "Map2_North_Spiral_Grotto", new Vector3(-13, 7, 22), new Vector3(12, 5, 9), m_Edge, false);
            ExploratoryNook(root, "Map2_East_Broken_Sanctum", new Vector3(24, 4, 5), new Vector3(11, 7, 10), m_Landmark, true);
            ExploratoryNook(root, "Map2_South_Siphon_Cleft", new Vector3(9, -12, -31), new Vector3(12, 5, 8), m_Edge, false);
            CompassBloom(root, "Map2_Visible_Reef_Bloom", new Vector3(3, 8, 9), 2.4f);
            Box(root, "Map2_Broken_Keel_Route_01", new Vector3(-21, 3, -2), new Vector3(8, 0.8f, 4), m_Bomb);
            Box(root, "Map2_Broken_Keel_Route_02", new Vector3(-4, 8, -21), new Vector3(7, 0.8f, 4), m_Landmark);
            Box(root, "Map2_Broken_Keel_Route_03", new Vector3(24, 4, -16), new Vector3(8, 0.8f, 4), m_Edge);
            Box(root, "Map2_Deep_Shell_Shortcut", new Vector3(0, -15, -25), new Vector3(11, 0.7f, 4), m_PinkWall);
        }

        // Map3 / level 4: a wide reef-ruin district for emergent three-bubble
        // combinations. The same central gulf can be crossed by building a bubble
        // chain, using a broad ruin route, or combining bounce with blast steering.
        private static void BuildMap3Geometry(Transform root)
        {
            Box(root, "Reef_West_Headland", new Vector3(-16, -0.4f, 0), new Vector3(18, 1, 30), m_Ground);
            Box(root, "Reef_Central_Ring", new Vector3(0, -1.1f, 1), new Vector3(16, 1, 19), m_Ground);
            Box(root, "Reef_North_Shelf", new Vector3(0, 2.6f, 16), new Vector3(28, 1, 12), m_Ground);
            Box(root, "Reef_East_Ridge", new Vector3(16, 1.2f, 1), new Vector3(15, 1, 25), m_Ground);
            Box(root, "Reef_South_Lagoon_Floor", new Vector3(1, -4.4f, -15), new Vector3(34, 1, 11), m_Edge);
            Box(root, "Reef_Lagoon_Step_A", new Vector3(-1, -2.6f, -8), new Vector3(12, 0.65f, 3.4f), m_Edge);
            Box(root, "Reef_Lagoon_Step_B", new Vector3(6, -3.5f, -11), new Vector3(10, 0.65f, 3.4f), m_Edge);
            Box(root, "Reef_Abyss_Backdrop_NoCollider", new Vector3(0, -10, 0), new Vector3(53, 0.15f, 45), m_Void, false);

            // First open neighborhood: a low plaza with several readable ways up.
            Box(root, "West_Start_Camp", new Vector3(-20, 0.2f, -5), new Vector3(7, 0.35f, 6), m_Landmark);
            RuinRoom(root, "West_Basalt_Grotto", new Vector3(-18, 0, 8), new Vector3(8, 5, 7), m_Edge, true);
            Box(root, "West_Grotto_Coral_Ledge", new Vector3(-18, 2.9f, 8), new Vector3(8.2f, 0.45f, 7.2f), m_Landmark);
            Box(root, "West_Ruin_Breakable_Facade", new Vector3(-13.7f, 1.3f, 8), new Vector3(0.4f, 2.6f, 4), m_PinkWall);
            Box(root, "West_Slope_Terrace_01", new Vector3(-10, 0.4f, 3), new Vector3(6, 0.7f, 5), m_Edge);
            Box(root, "West_Slope_Terrace_02", new Vector3(-7, 1.1f, 1), new Vector3(5, 0.7f, 4), m_Edge);

            // A central open basin offers a risky direct leap or several large
            // anchor placements. Bubble props are primitive placeholders only.
            Sphere(root, "Gulf_Anchor_01", new Vector3(-7, 1.3f, -1), 1.5f, m_Sticky);
            Sphere(root, "Gulf_Anchor_02", new Vector3(-3, 2.1f, 1), 1.5f, m_Sticky);
            Sphere(root, "Gulf_Anchor_03", new Vector3(1, 2.7f, 2), 1.5f, m_Sticky);
            Sphere(root, "Gulf_Anchor_04", new Vector3(5, 2.2f, 1), 1.5f, m_Sticky);
            Sphere(root, "Gulf_Bounce_Surface_A", new Vector3(-2, 0.2f, -4), 2.2f, m_Bounce);
            Sphere(root, "Gulf_Bounce_Surface_B", new Vector3(3, 0.8f, -2), 2.2f, m_Bounce);
            Sphere(root, "Gulf_Explosive_Bubble_Visual", new Vector3(1, 1.2f, -5), 1.6f, m_Bomb);
            Box(root, "Central_Ruin_Beam_Prop", new Vector3(1, 0.8f, 5), new Vector3(8, 0.6f, 1.1f), m_Bomb);
            Box(root, "Central_Ruin_Beam_Support_L", new Vector3(-2.5f, 0.6f, 5), new Vector3(0.55f, 1.2f, 0.55f), m_PinkWall);
            Box(root, "Central_Ruin_Beam_Support_R", new Vector3(4.5f, 0.6f, 5), new Vector3(0.55f, 1.2f, 0.55f), m_PinkWall);

            // North market and east tower district connect around both sides of
            // the gulf. Soft pink walls are opportunities, not a key-lock gate.
            RuinRoom(root, "North_Coral_Vault", new Vector3(-1, 2.8f, 17), new Vector3(9, 5, 7), m_Landmark, true);
            RuinRoom(root, "North_Tide_Grotto", new Vector3(9, 2.8f, 17), new Vector3(7, 4, 6), m_Edge, false);
            Box(root, "North_Coral_Breakable_Crust", new Vector3(4, 4.2f, 17), new Vector3(0.45f, 2.4f, 3.5f), m_PinkWall);
            Box(root, "North_Coral_Ridge_Bridge", new Vector3(5, 5.5f, 13), new Vector3(7, 0.45f, 2), m_Sticky);
            RuinArch(root, "East_Ridge_Arch", new Vector3(15, 1.2f, 9), 6, 5, m_Edge);
            RuinRoom(root, "East_Lantern_Coral_Chimney", new Vector3(18, 1.4f, 14), new Vector3(7, 8, 7), m_Landmark, true);
            Box(root, "East_Chimney_Coral_Perch", new Vector3(18, 5.7f, 14), new Vector3(7.5f, 0.45f, 7.5f), m_Bounce);
            Box(root, "East_Chimney_Pink_Crust", new Vector3(18, 2.5f, 14), new Vector3(0.4f, 3.2f, 3.8f), m_PinkWall);

            // Lower lagoon loop reconnects to the west headland and east ridge.
            Box(root, "Lagoon_West_Arch", new Vector3(-9, -2.1f, -14), new Vector3(5, 0.55f, 3), m_Edge);
            Box(root, "Lagoon_Mid_Rubble_Island", new Vector3(0, -2.7f, -16), new Vector3(6, 0.65f, 5), m_Landmark);
            Box(root, "Lagoon_East_Pier", new Vector3(10, -2.2f, -14), new Vector3(7, 0.55f, 3), m_Edge);
            Sphere(root, "Lagoon_Anchor_01", new Vector3(-5, -0.3f, -12), 1.3f, m_Sticky);
            Sphere(root, "Lagoon_Anchor_02", new Vector3(1, 0.5f, -12), 1.3f, m_Sticky);
            Box(root, "Lagoon_Pink_Sluice_Panel", new Vector3(13, -1.2f, -9), new Vector3(0.45f, 3.2f, 5.2f), m_PinkWall);
            Box(root, "Reef_Outer_Cliff", new Vector3(0, 1, 25), new Vector3(54, 3, 0.8f), m_Void);

            ExploratoryNook(root, "Map3_North_Brine_Grotto", new Vector3(-13, 10, 25), new Vector3(13, 6, 10), m_Landmark, true);
            ExploratoryNook(root, "Map3_East_Whistle_Chimney", new Vector3(27, 8, 10), new Vector3(11, 9, 10), m_Edge, false);
            ExploratoryNook(root, "Map3_South_Blue_Hole", new Vector3(3, -14, -27), new Vector3(13, 7, 9), m_Landmark, false);
            CompassBloom(root, "Map3_Central_Current_Bloom", new Vector3(2, 7, 3), 2.2f);
            Box(root, "Map3_Corkscrew_Upper_Exit_Shelf", new Vector3(2, 19, 24), new Vector3(14, 0.8f, 8), m_Bounce);
            Box(root, "Map3_Blue_Hole_Optional_Lower_Rim", new Vector3(14, -18, -24), new Vector3(10, 0.8f, 6), m_Sticky);

            // Five reefs form a corkscrew descent around the gulf. Each fall has
            // a broad shelf, but the fast route requires a short bubble flight
            // followed by a mid-air blast correction between the coral teeth.
            Box(root, "Corkscrew_Shelf_01", new Vector3(-21, 7, 12), new Vector3(12, 0.8f, 8), m_Landmark);
            Box(root, "Corkscrew_Shelf_02", new Vector3(-9, 4, 8), new Vector3(9, 0.8f, 6), m_Edge);
            Box(root, "Corkscrew_Shelf_03", new Vector3(3, 1, 12), new Vector3(10, 0.8f, 7), m_Landmark);
            Box(root, "Corkscrew_Shelf_04", new Vector3(15, -2, 7), new Vector3(9, 0.8f, 6), m_Edge);
            Box(root, "Corkscrew_Shelf_05", new Vector3(20, -5, -1), new Vector3(10, 0.8f, 7), m_Landmark);
            CoralColony(root, "Corkscrew_Coral_Tooth_A", new Vector3(-3, 0, 8), 4.5f, m_Bounce);
            CoralColony(root, "Corkscrew_Coral_Tooth_B", new Vector3(10, -2, 2), 3.6f, m_Sticky);
            Box(root, "Midair_Correction_Pink_Crust", new Vector3(8, 0.5f, 0), new Vector3(4, 0.45f, 2.8f), m_PinkWall);
            Box(root, "Undercut_Reef_Arch_Upper", new Vector3(-8, -6, -15), new Vector3(10, 0.8f, 4), m_Edge);
            Box(root, "Undercut_Reef_Arch_Lower", new Vector3(8, -9, -18), new Vector3(12, 0.8f, 4), m_Landmark);
            Sphere(root, "Bubble_Chain_Anchor_05", new Vector3(-1, -4, -15), 1.8f, m_Sticky);
            Sphere(root, "Bubble_Chain_Anchor_06", new Vector3(5, -1, -15), 1.8f, m_Sticky);
        }

        // Map4 / level 5: an expansive memory reef with three interlocking layers.
        // The resonance object sits in the middle canopy, but several different
        // neighborhoods can reach it; the low trench and high crown are optional
        // discoveries. Keep each vertical transfer bounded by a broad landing.
        private static void BuildMap4Geometry(Transform root)
        {
            Box(root, "Resonance_West_Cliff", new Vector3(-19, 0, 0), new Vector3(17, 1, 37), m_Ground);
            Box(root, "Resonance_Central_Garden", new Vector3(0, 0.25f, 0), new Vector3(21, 1, 28), m_Ground);
            Box(root, "Resonance_East_Ridge", new Vector3(20, 1.4f, 2), new Vector3(17, 1, 31), m_Ground);
            Box(root, "Resonance_North_Crown", new Vector3(0, 3.3f, 20), new Vector3(35, 1, 13), m_Ground);
            Box(root, "Resonance_South_Trench", new Vector3(1, -4.2f, -19), new Vector3(37, 1, 10), m_Edge);
            Box(root, "Resonance_Trench_Shelf_W", new Vector3(-8, -2.3f, -10), new Vector3(10, 0.7f, 4), m_Edge);
            Box(root, "Resonance_Trench_Shelf_E", new Vector3(8, -2.7f, -11), new Vector3(10, 0.7f, 4), m_Edge);
            Box(root, "Resonance_Abyss_Backdrop_NoCollider", new Vector3(0, -10, 0), new Vector3(62, 0.15f, 55), m_Void, false);

            Box(root, "Start_Memory_Beach", new Vector3(-22, 0.7f, -11), new Vector3(8, 0.35f, 6), m_Landmark);
            RuinRoom(root, "West_Coral_Rib_Cavern", new Vector3(-19, 0.2f, 7), new Vector3(8, 6, 8), m_Edge, true);
            Box(root, "West_Cavern_Upper_Ridge", new Vector3(-19, 3.7f, 7), new Vector3(8.4f, 0.45f, 8.4f), m_Landmark);
            Box(root, "West_Cavern_Pink_Crust", new Vector3(-14.6f, 1.8f, 7), new Vector3(0.45f, 3.5f, 4.5f), m_PinkWall);
            RuinArch(root, "West_Cliff_Arch", new Vector3(-10, 0, 2), 6, 5, m_Bounce);

            // Central atrium has a destructible short-cut wall and loose mass props.
            Box(root, "Atrium_Round_Platform", new Vector3(0, 0.8f, 1), new Vector3(9, 0.6f, 8), m_Landmark);
            Box(root, "Atrium_Column_A", new Vector3(-6, 2.4f, -5), new Vector3(0.8f, 4.8f, 0.8f), m_Edge);
            Box(root, "Atrium_Column_B", new Vector3(6, 2.1f, -5), new Vector3(0.8f, 4.2f, 0.8f), m_Edge);
            Box(root, "Atrium_Pink_Wall_01", new Vector3(0, 1.6f, -8.3f), new Vector3(7.5f, 3.2f, 0.45f), m_PinkWall);
            Box(root, "Atrium_Pink_Wall_02", new Vector3(7.6f, 1.6f, 0), new Vector3(0.45f, 3.2f, 7.5f), m_PinkWall);
            Box(root, "Atrium_Heavy_Statue", new Vector3(3.2f, 1.7f, 3.2f), new Vector3(2.2f, 3.4f, 2.2f), m_Bomb);
            Sphere(root, "Atrium_Float_Bubble_A", new Vector3(-3.5f, 2.8f, 0), 1.6f, m_Sticky);
            Sphere(root, "Atrium_Float_Bubble_B", new Vector3(0, 3.8f, -1.8f), 1.6f, m_Sticky);
            Sphere(root, "Atrium_Float_Bubble_C", new Vector3(3.5f, 4.5f, -1.2f), 1.6f, m_Sticky);
            Box(root, "Atrium_Beam_Prop", new Vector3(0, 2.4f, 9), new Vector3(10, 0.65f, 1.2f), m_Bomb);
            Box(root, "Atrium_Beam_Support_A", new Vector3(-4, 1.9f, 9), new Vector3(0.55f, 2.8f, 0.55f), m_PinkWall);
            Box(root, "Atrium_Beam_Support_B", new Vector3(4, 1.9f, 9), new Vector3(0.55f, 2.8f, 0.55f), m_PinkWall);

            // Side districts form a loop around the center; no single entrance is
            // mandatory. Their broad roof decks create stable fallbacks after a
            // limited float burst expires.
            RuinRoom(root, "North_Calcite_Cavern", new Vector3(-6, 3.4f, 20), new Vector3(10, 6, 8), m_Landmark, true);
            Box(root, "North_Coral_Canopy_Ridge", new Vector3(-6, 6.7f, 20), new Vector3(10.5f, 0.45f, 8.5f), m_Bounce);
            RuinRoom(root, "North_Memory_Grottoes", new Vector3(8, 3.4f, 20), new Vector3(9, 5, 7), m_Edge, false);
            Box(root, "North_Grotto_Pink_Crust", new Vector3(3.3f, 4.5f, 20), new Vector3(0.45f, 2.4f, 4), m_PinkWall);
            RuinRoom(root, "East_Vent_Cavern", new Vector3(20, 1.8f, 9), new Vector3(9, 7, 9), m_Landmark, true);
            Box(root, "East_Vent_Coral_Ridge", new Vector3(20, 5.9f, 9), new Vector3(9.5f, 0.45f, 9.5f), m_Edge);
            Box(root, "East_Vent_Pink_Crust", new Vector3(15.3f, 2.7f, 9), new Vector3(0.45f, 3.5f, 4.4f), m_PinkWall);

            // Resonance marker is intentionally midway in the map, not at the far
            // edge; the top crown and bottom trench remain optional discoveries.
            Box(root, "Resonance_Core_Plinth", new Vector3(1, 1.5f, 1), new Vector3(4.5f, 0.55f, 4.5f), m_Sticky);
            Box(root, "Resonance_Core_Stem", new Vector3(1, 3.5f, 1), new Vector3(0.85f, 3.8f, 0.85f), m_Landmark);
            Sphere(root, "Resonance_Core_Visual", new Vector3(1, 5.6f, 1), 1.8f, m_Bounce);
            Box(root, "Crown_Upper_Deck", new Vector3(0, 7.1f, 21), new Vector3(12, 0.55f, 6), m_Landmark);
            Box(root, "Trench_Memory_Shard", new Vector3(1, -3.2f, -19), new Vector3(4.2f, 0.5f, 3.2f), m_Bounce);
            Sphere(root, "Trench_Anchor_A", new Vector3(-6, -0.7f, -15), 1.5f, m_Sticky);
            Sphere(root, "Trench_Anchor_B", new Vector3(0, 0.2f, -14), 1.5f, m_Sticky);
            Sphere(root, "Trench_Anchor_C", new Vector3(6, -0.3f, -15), 1.5f, m_Sticky);

            // Distinct ledges create seven traversable strata. A narrow chimney
            // links trench to canopy; the wide outer spiral is safer but longer.
            Box(root, "Deep_Trench_Second_Shelf", new Vector3(-15, -8, -22), new Vector3(10, 0.8f, 5), m_Landmark);
            Box(root, "Deep_Trench_Third_Shelf", new Vector3(12, -11, -21), new Vector3(12, 0.8f, 5), m_Edge);
            Box(root, "Midwater_Coral_Balcony_W", new Vector3(-24, 5, 5), new Vector3(10, 0.8f, 7), m_Bounce);
            Box(root, "Midwater_Coral_Balcony_N", new Vector3(-3, 8, 27), new Vector3(14, 0.8f, 6), m_Landmark);
            Box(root, "Midwater_Coral_Balcony_E", new Vector3(26, 5, 5), new Vector3(10, 0.8f, 7), m_Bounce);
            Box(root, "Canopy_Upper_Shelf_A", new Vector3(-18, 13, 17), new Vector3(13, 0.8f, 7), m_Edge);
            Box(root, "Canopy_Upper_Shelf_B", new Vector3(4, 16, 24), new Vector3(14, 0.8f, 7), m_Landmark);
            Box(root, "Canopy_Upper_Shelf_C", new Vector3(23, 13, 17), new Vector3(12, 0.8f, 7), m_Edge);
            RockSpire(root, "Vertical_Chimney_Rib_A", new Vector3(-4, 0, 11), 13, 2, -12, m_Edge);
            RockSpire(root, "Vertical_Chimney_Rib_B", new Vector3(5, 2, 11), 15, 2, 15, m_Landmark);
            Box(root, "Resonance_Three_Node_Bridge", new Vector3(1, 8.7f, 14), new Vector3(16, 0.65f, 2), m_Sticky);
            Sphere(root, "Resonance_Node_Left", new Vector3(-6, 10, 14), 1.7f, m_Bounce);
            Sphere(root, "Resonance_Node_Right", new Vector3(8, 10, 14), 1.7f, m_Bounce);
            Box(root, "Resonance_Node_Thin_Crust", new Vector3(1, 9.5f, 14), new Vector3(0.5f, 1.8f, 2.3f), m_PinkWall);

            ExploratoryNook(root, "Map4_West_Whale_Bone_Grotto", new Vector3(-31, 9, 11), new Vector3(15, 8, 12), m_Landmark, true);
            ExploratoryNook(root, "Map4_North_Tidepool_Overhang", new Vector3(2, 19, 36), new Vector3(17, 7, 12), m_Edge, false);
            ExploratoryNook(root, "Map4_Abyssal_Eye", new Vector3(3, -21, 4), new Vector3(17, 8, 13), m_Landmark, false);
            CompassBloom(root, "Map4_Hanging_Coral_Eye", new Vector3(5, 13, 17), 2.7f);
            Box(root, "Map4_Upper_Ridge_Optional_Loop", new Vector3(-11, 21, 28), new Vector3(15, 0.8f, 7), m_Bounce);
            Box(root, "Map4_Abyssal_Eye_Exit_Ridge", new Vector3(19, -18, 10), new Vector3(12, 0.8f, 7), m_Sticky);
        }

        // Map5: the largest open region so far, a submerged memory district built
        // from connected coves, ruins and a sunken town. The player can loop between
        // districts, open shortcuts by breaking ordinary architecture, repurpose
        // columns/beams as traversable geometry, and choose a low, middle or crown
        // approach. Vertical excursions always have nearby broad fallback shelves.
        private static void BuildMap5Geometry(Transform root)
        {
            Box(root, "Memory_Basin_West", new Vector3(-18, -0.7f, 0), new Vector3(19, 1, 35), m_Ground);
            Box(root, "Memory_Basin_Central", new Vector3(2, -0.4f, 0), new Vector3(22, 1, 31), m_Ground);
            Box(root, "Memory_Basin_East", new Vector3(22, 0.9f, 0), new Vector3(17, 1, 33), m_Ground);
            Box(root, "Memory_Basin_North_Causeway", new Vector3(2, 2.7f, 19), new Vector3(42, 1, 12), m_Ground);
            Box(root, "Memory_Basin_South_Shelf", new Vector3(4, -3.2f, -19), new Vector3(39, 1, 11), m_Edge);
            Box(root, "Memory_Basin_Lower_Shelf_A", new Vector3(-8, -1.9f, -10), new Vector3(9, 0.65f, 4), m_Edge);
            Box(root, "Memory_Basin_Lower_Shelf_B", new Vector3(6, -2.2f, -11), new Vector3(11, 0.65f, 4), m_Edge);
            Box(root, "Memory_Basin_Lower_Shelf_C", new Vector3(21, -1.3f, -10), new Vector3(9, 0.65f, 4), m_Edge);
            Box(root, "Memory_Basin_Abyss_Backdrop_NoCollider", new Vector3(2, -10, 0), new Vector3(70, 0.15f, 58), m_Void, false);

            Box(root, "Start_Outer_Courtyard", new Vector3(-24, 0.1f, -13), new Vector3(9, 0.35f, 7), m_Landmark);
            RuinArch(root, "West_Cove_Arch", new Vector3(-19, -0.2f, -4), 6, 5, m_Edge);
            RuinRoom(root, "West_Cove_Grotto_A", new Vector3(-23, -0.2f, 7), new Vector3(8, 5, 7), m_Edge, true);
            RuinRoom(root, "West_Cove_Grotto_B", new Vector3(-13, -0.2f, 10), new Vector3(7, 4, 6), m_Landmark, false);
            Box(root, "West_Cove_Pink_Calcite_Seam", new Vector3(-8.2f, 1.1f, 7), new Vector3(0.45f, 2.2f, 6), m_PinkWall);
            Box(root, "West_Cove_Coral_Ridge_Crossing", new Vector3(-12, 2.8f, 7), new Vector3(11, 0.45f, 2.5f), m_Sticky);

            // Sunken market with several small blocks and continuous alleys.
            RuinRoom(root, "Market_Basalt_Cavern_A", new Vector3(-3, 0, 10), new Vector3(8, 6, 8), m_Landmark, true);
            RuinRoom(root, "Market_Basalt_Cavern_B", new Vector3(7, 0, 11), new Vector3(7, 5, 7), m_Edge, false);
            RuinRoom(root, "Market_Basalt_Cavern_C", new Vector3(16, 1, 10), new Vector3(7, 6, 8), m_Landmark, true);
            Box(root, "Market_Pink_Calcite_Seam_A", new Vector3(1.4f, 1.2f, 5.8f), new Vector3(0.45f, 2.4f, 4.2f), m_PinkWall);
            Box(root, "Market_Pink_Calcite_Seam_B", new Vector3(11.1f, 1.2f, 6.8f), new Vector3(0.45f, 2.4f, 3.6f), m_PinkWall);
            Box(root, "Market_Main_Square", new Vector3(7, 0.25f, 1), new Vector3(10, 0.5f, 8), m_Ground);
            Box(root, "Market_Well_Base", new Vector3(7, 0.55f, 1), new Vector3(3.4f, 0.6f, 3.4f), m_Edge);
            Sphere(root, "Market_Well_Bubble", new Vector3(7, 1.8f, 1), 1.5f, m_Sticky);

            // West cliff / north causeway loop. The arch can be bypassed via the
            // low shelf or opened through the adjacent soft wall.
            RuinRoom(root, "North_Coral_Cathedral_Cavern", new Vector3(-7, 2.8f, 19), new Vector3(10, 7, 8), m_Landmark, true);
            Box(root, "North_Coral_Canopy", new Vector3(-7, 6.6f, 19), new Vector3(10.5f, 0.45f, 8.5f), m_Bounce);
            RuinArch(root, "North_Causeway_Arch", new Vector3(4, 2.7f, 19), 7, 6, m_Edge);
            RuinRoom(root, "North_Basalt_Vault", new Vector3(15, 2.8f, 19), new Vector3(8, 5, 8), m_Edge, false);
            Box(root, "North_Vault_Pink_Crust", new Vector3(10.8f, 4, 19), new Vector3(0.45f, 2.3f, 4.3f), m_PinkWall);

            // East observatory terraces: gradual stable landings make short float
            // bursts a choice rather than one long mandatory flight.
            Box(root, "East_Terrace_01", new Vector3(19, 1.5f, 2), new Vector3(10, 0.7f, 8), m_Edge);
            Box(root, "East_Terrace_02", new Vector3(22, 2.7f, -2), new Vector3(9, 0.7f, 7), m_Landmark);
            RuinRoom(root, "East_Vent_Cavern", new Vector3(23, 1.3f, 13), new Vector3(9, 8, 9), m_Landmark, true);
            Box(root, "East_Vent_Coral_Crown", new Vector3(23, 6, 13), new Vector3(9.5f, 0.45f, 9.5f), m_Bounce);
            Box(root, "East_Vent_Pink_Crust", new Vector3(18.4f, 2.7f, 13), new Vector3(0.45f, 3.4f, 4.6f), m_PinkWall);
            Sphere(root, "East_Anchor_Bubble_A", new Vector3(14, 3.2f, 1), 1.5f, m_Sticky);
            Sphere(root, "East_Anchor_Bubble_B", new Vector3(18, 4.1f, -1), 1.5f, m_Sticky);
            Sphere(root, "East_Anchor_Bubble_C", new Vector3(22, 4.8f, 0), 1.5f, m_Sticky);

            // A collapsed bridge is a wide traversable landmark. The standing
            // supports beside it invite players to infer how the structure broke.
            BoxRotated(root, "Sunken_Bridge_Fallen_Beam", new Vector3(2, -0.4f, -7), new Vector3(13, 0.75f, 2), new Vector3(0, 0, -8), m_Bomb);
            Box(root, "Sunken_Bridge_Support_A", new Vector3(-3, 0.6f, -7), new Vector3(0.75f, 2.2f, 0.75f), m_PinkWall);
            Box(root, "Sunken_Bridge_Support_B", new Vector3(7, 0.6f, -7), new Vector3(0.75f, 2.2f, 0.75f), m_PinkWall);
            Box(root, "South_Rubble_Causeway", new Vector3(-1, -2.4f, -18), new Vector3(12, 0.65f, 3.5f), m_Landmark);
            Sphere(root, "South_Cove_Anchor_A", new Vector3(-10, -0.2f, -14), 1.4f, m_Sticky);
            Sphere(root, "South_Cove_Anchor_B", new Vector3(-3, 0.5f, -14), 1.4f, m_Sticky);
            Sphere(root, "South_Cove_Anchor_C", new Vector3(4, 0.1f, -15), 1.4f, m_Sticky);

            // Several points of interest communicate exploration without making
            // the end platform the sole reason to traverse the region.
            Box(root, "Memory_Garden_Plinth", new Vector3(0, 1, 0), new Vector3(5, 0.55f, 5), m_Sticky);
            Box(root, "Memory_Garden_Stem", new Vector3(0, 3.2f, 0), new Vector3(0.9f, 4, 0.9f), m_Landmark);
            Sphere(root, "Memory_Garden_Core", new Vector3(0, 5.5f, 0), 1.8f, m_Bounce);
            Box(root, "Optional_Low_Memory_Shard", new Vector3(4, -2.8f, -18), new Vector3(4.2f, 0.5f, 3.2f), m_Landmark);
            Box(root, "Outer_Cliff_North", new Vector3(2, 1.5f, 29), new Vector3(69, 4, 0.8f), m_Void);

            // Map5's deepest route wraps under the city before rising through a
            // coral chimney. The upper ceiling reef has a different, narrower
            // loop; players can splice the routes at two intermediate saddles.
            Box(root, "Abyssal_Shelf_01", new Vector3(-21, -7, -24), new Vector3(13, 0.8f, 6), m_Edge);
            Box(root, "Abyssal_Shelf_02", new Vector3(-2, -11, -28), new Vector3(12, 0.8f, 5), m_Landmark);
            Box(root, "Abyssal_Shelf_03", new Vector3(19, -8, -23), new Vector3(14, 0.8f, 6), m_Edge);
            Box(root, "Abyssal_Shelf_04", new Vector3(30, -4, -14), new Vector3(10, 0.8f, 6), m_Landmark);
            RockSpire(root, "Abyssal_Coral_Chimney_W", new Vector3(-6, -4, -13), 14, 2.4f, -9, m_Bounce);
            RockSpire(root, "Abyssal_Coral_Chimney_E", new Vector3(6, 0, -13), 17, 2.1f, 11, m_Sticky);
            Box(root, "Abyssal_Chimney_Squeeze", new Vector3(0, 5.5f, -13), new Vector3(4.4f, 0.6f, 2.1f), m_PinkWall);
            Box(root, "Ceiling_Reef_Shelf_W", new Vector3(-25, 13, 15), new Vector3(14, 0.8f, 8), m_Landmark);
            Box(root, "Ceiling_Reef_Shelf_C", new Vector3(0, 17, 25), new Vector3(17, 0.8f, 7), m_Bounce);
            Box(root, "Ceiling_Reef_Shelf_E", new Vector3(24, 13, 15), new Vector3(13, 0.8f, 8), m_Landmark);
            Box(root, "Ceiling_Reef_Cracked_Bridge", new Vector3(0, 14, 14), new Vector3(12, 0.65f, 1.7f), m_Bomb);
            Box(root, "Ceiling_Reef_Breakable_Seam", new Vector3(0, 14, 13), new Vector3(3.5f, 0.45f, 2.2f), m_PinkWall);
            CoralColony(root, "Ceiling_Reef_Coral_Forest", new Vector3(2, 17, 29), 5.3f, m_Bounce);
            Box(root, "Cross_Layer_Saddle_W", new Vector3(-14, 4, 1), new Vector3(10, 0.8f, 6), m_Edge);
            Box(root, "Cross_Layer_Saddle_E", new Vector3(15, 4, 1), new Vector3(10, 0.8f, 6), m_Landmark);
            Sphere(root, "Cross_Layer_Anchor_D", new Vector3(-10, 7, -4), 1.9f, m_Sticky);
            Sphere(root, "Cross_Layer_Anchor_E", new Vector3(1, 10, -3), 1.9f, m_Sticky);
            Sphere(root, "Cross_Layer_Anchor_F", new Vector3(12, 7, -4), 1.9f, m_Sticky);

            ExploratoryNook(root, "Map5_West_Leviathan_Grotto", new Vector3(-37, 11, 12), new Vector3(17, 9, 14), m_Landmark, true);
            ExploratoryNook(root, "Map5_East_Black_Smoker_Field", new Vector3(39, 5, 13), new Vector3(16, 11, 13), m_Edge, false);
            ExploratoryNook(root, "Map5_Deep_Anemone_Sea", new Vector3(4, -24, -39), new Vector3(19, 8, 12), m_Landmark, false);
            CompassBloom(root, "Map5_Abyssal_Coral_Heart", new Vector3(1, 12, 10), 3.2f);
            Box(root, "Map5_Summit_Optional_Ridge", new Vector3(1, 25, 39), new Vector3(18, 0.9f, 10), m_Bounce);
            Box(root, "Map5_South_Abyss_Optional_Ridge", new Vector3(22, -24, -33), new Vector3(15, 0.9f, 8), m_Sticky);
            Box(root, "Map5_Outer_Reef_Return_Loop_W", new Vector3(-36, 1, -4), new Vector3(12, 0.8f, 6), m_Edge);
            Box(root, "Map5_Outer_Reef_Return_Loop_E", new Vector3(38, 1, -3), new Vector3(12, 0.8f, 6), m_Landmark);
        }

        private static void RuinRoom(Transform root, string name, Vector3 center, Vector3 size, Material material, bool brokenCorner)
        {
            // This helper used to make rectilinear rooms. In the underwater
            // blockout it now makes an open limestone cavern: fractured shelves,
            // ribs, a readable ceiling line and a broken shell/crust seam.
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float floorY = center.y + 0.15f;
            Box(root, name + "_Cavern_Floor", new Vector3(center.x, floorY, center.z), new Vector3(size.x, 0.35f, size.z), m_Ground);
            BoxRotated(root, name + "_Ridge_Shelf_W", new Vector3(center.x - halfX * 0.62f, floorY + size.y * 0.22f, center.z - halfZ * 0.08f), new Vector3(size.x * 0.34f, 0.65f, size.z * 0.8f), new Vector3(0, 0, -9), material);
            BoxRotated(root, name + "_Ridge_Shelf_E", new Vector3(center.x + halfX * 0.62f, floorY + size.y * 0.34f, center.z + halfZ * 0.1f), new Vector3(size.x * 0.28f, 0.65f, size.z * 0.74f), new Vector3(0, 0, 12), m_Landmark);
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                float x = center.x - halfX * 0.78f + t * size.x * 1.56f;
                float z = center.z + Mathf.Sin((i + 1) * 1.7f) * halfZ * 0.52f;
                float h = size.y * (0.46f + (i % 3) * 0.12f);
                RockSpire(root, name + "_Rib_" + i, new Vector3(x, center.y + h * 0.5f, z), h, size.z * (0.12f + (i % 2) * 0.04f), i * 11f, material);
            }
            // Stalactites and stalagmites frame a deep central void; the missing
            // pieces are deliberate swim-through passages rather than doors.
            for (int i = 0; i < 4; i++)
            {
                float x = center.x + (i - 1.5f) * size.x * 0.24f;
                float z = center.z + Mathf.Cos(i * 2.1f) * halfZ * 0.34f;
                RockSpire(root, name + "_Ceiling_Tooth_" + i, new Vector3(x, center.y + size.y * 0.78f, z), size.y * 0.34f, size.z * 0.1f, i * 17f, m_Edge);
            }
            if (brokenCorner)
            {
                // Pink calcite crust is a conspicuous optional breach. It sits
                // across one narrow side cleft while a longer open bypass stays visible.
                BoxRotated(root, name + "_Breakable_Calcite_Crust", new Vector3(center.x + halfX * 0.78f, center.y + size.y * 0.36f, center.z + halfZ * 0.7f), new Vector3(size.x * 0.1f, size.y * 0.7f, 0.45f), new Vector3(0, 12, -6), m_PinkWall);
                Box(root, name + "_Collapse_Rubble_A", new Vector3(center.x - halfX * 0.56f, floorY + 0.35f, center.z - halfZ * 0.7f), new Vector3(2.1f, 0.7f, 1.3f), m_Edge);
                BoxRotated(root, name + "_Collapse_Rubble_B", new Vector3(center.x + halfX * 0.28f, floorY + 0.25f, center.z - halfZ * 0.78f), new Vector3(2.5f, 0.55f, 1.2f), new Vector3(0, 0, 14), m_Landmark);
            }
            CoralColony(root, name + "_Coral_Colony", new Vector3(center.x - halfX * 0.15f, floorY, center.z + halfZ * 0.58f), Mathf.Min(size.x, size.z) * 0.28f, m_Bounce);
        }

        private static void ExploratoryNook(Transform root, string name, Vector3 center, Vector3 size, Material material, bool isRuin)
        {
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            Box(root, name + (isRuin ? "_Broken_Ruin_Plinth" : "_Basalt_Shelf"), center,
                new Vector3(size.x, 0.6f, size.z), material);
            BoxRotated(root, name + "_Reachable_Climb_Ridge_A",
                new Vector3(center.x - halfX * 0.62f, center.y + size.y * 0.36f, center.z - halfZ * 0.15f),
                new Vector3(size.x * 0.44f, 0.7f, size.z * 0.45f), new Vector3(0, 0, -12), m_Edge);
            BoxRotated(root, name + "_Reachable_Climb_Ridge_B",
                new Vector3(center.x + halfX * 0.42f, center.y + size.y * 0.7f, center.z + halfZ * 0.28f),
                new Vector3(size.x * 0.4f, 0.7f, size.z * 0.4f), new Vector3(0, 0, 14), m_Landmark);
            Box(root, name + "_Bomb_Breakable_Debris_Visual",
                new Vector3(center.x + halfX * 0.68f, center.y + 1f, center.z - halfZ * 0.45f),
                new Vector3(1.7f, 1.7f, 1.7f), m_Bomb);
            RockSpire(root, name + "_Sticky_Coral_Pylon",
                new Vector3(center.x - halfX * 0.36f, center.y, center.z + halfZ * 0.34f),
                size.y, Mathf.Min(size.x, size.z) * 0.16f, 8f, m_Sticky);
            CoralColony(root, name + "_Reef_Climb_Silhouette",
                new Vector3(center.x + halfX * 0.14f, center.y + 0.2f, center.z + halfZ * 0.52f),
                Mathf.Min(size.x, size.z) * 0.3f, m_Bounce);
            if (isRuin)
            {
                Box(root, name + "_Breakable_Rubble_Seam",
                    new Vector3(center.x - halfX * 0.7f, center.y + 1.2f, center.z),
                    new Vector3(0.55f, 2.4f, size.z * 0.48f), m_PinkWall);
            }
        }

        private static void CompassBloom(Transform root, string name, Vector3 center, float scale)
        {
            Sphere(root, name + "_Core", center, scale, m_Sticky);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f + 45f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(radians) * scale * 2.8f, i % 2 == 0 ? 1.3f : -1.3f, Mathf.Sin(radians) * scale * 2.8f);
                BoxRotated(root, name + "_Arm_" + i, center + offset,
                    new Vector3(scale * 2.4f, 0.65f, scale * 1.7f), new Vector3(0, angle, i % 2 == 0 ? 12f : -12f),
                    i % 2 == 0 ? m_Bounce : m_Edge);
                CoralColony(root, name + "_Arm_Coral_" + i, center + offset + Vector3.up * 0.4f, scale * 0.85f, m_Bounce);
            }
        }

        private static void RuinArch(Transform root, string name, Vector3 center, float width, float height, Material material)
        {
            RockSpire(root, name + "_Coral_Rock_Left", new Vector3(center.x - width * 0.42f, center.y + height * 0.46f, center.z), height * 0.9f, 1.4f, -7f, material);
            RockSpire(root, name + "_Coral_Rock_Right", new Vector3(center.x + width * 0.42f, center.y + height * 0.42f, center.z + 0.3f), height * 0.82f, 1.7f, 9f, m_Landmark);
            BoxRotated(root, name + "_Broken_Keystone", new Vector3(center.x, center.y + height * 0.96f, center.z), new Vector3(width * 0.68f, 0.9f, 1.3f), new Vector3(0, 0, -8), material);
            CoralColony(root, name + "_Reef_Growth", new Vector3(center.x + width * 0.18f, center.y, center.z + 0.9f), width * 0.22f, m_Bounce);
        }

        private static void RockSpire(Transform root, string name, Vector3 basePosition, float height, float diameter, float lean, Material material)
        {
            BoxRotated(root, name + "_Foot", basePosition + new Vector3(0, height * 0.12f, 0), new Vector3(diameter * 1.45f, height * 0.28f, diameter * 1.25f), new Vector3(0, lean, -lean * 0.25f), material);
            BoxRotated(root, name + "_Body", basePosition + new Vector3(0, height * 0.44f, 0), new Vector3(diameter, height * 0.62f, diameter * 0.88f), new Vector3(lean, lean * 0.3f, -lean), material);
            BoxRotated(root, name + "_Crown", basePosition + new Vector3(0, height * 0.83f, 0), new Vector3(diameter * 0.58f, height * 0.36f, diameter * 0.62f), new Vector3(-lean * 0.5f, lean, lean * 0.7f), m_Landmark);
        }

        private static void CoralColony(Transform root, string name, Vector3 basePosition, float scale, Material material)
        {
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 72f;
                float radians = angle * Mathf.Deg2Rad;
                float length = scale * (0.72f + (i % 3) * 0.18f);
                Vector3 offset = new Vector3(Mathf.Cos(radians) * scale * 0.34f, length * 0.46f, Mathf.Sin(radians) * scale * 0.34f);
                Vector3 branchScale = new Vector3(scale * 0.14f, length, scale * 0.14f);
                BoxRotated(root, name + "_Branch_" + i, basePosition + offset, branchScale, new Vector3(Mathf.Sin(radians) * 22f, angle, Mathf.Cos(radians) * -28f), material);
                Sphere(root, name + "_Tip_" + i, basePosition + offset * 1.85f, scale * 0.2f, material);
            }
            Sphere(root, name + "_Heart", basePosition + Vector3.up * scale * 0.3f, scale * 0.38f, material);
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

        private static GameObject BoxRotated(Transform parent, string name, Vector3 position, Vector3 size, Vector3 euler, Material material)
        {
            GameObject go = Box(parent, name, position, size, material);
            go.transform.rotation = Quaternion.Euler(euler);
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
