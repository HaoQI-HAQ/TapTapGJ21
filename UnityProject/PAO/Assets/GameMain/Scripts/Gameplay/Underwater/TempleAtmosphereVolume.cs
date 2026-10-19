using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.Underwater
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class TempleAtmosphereVolume : MonoBehaviour
    {
        [Tooltip("Scene-specific water haze material. Caustics use the main light cookie, as in Map4.")]
        public Material material;

        static readonly Dictionary<int, TempleAtmosphereVolume> Volumes = new Dictionary<int, TempleAtmosphereVolume>();

        void OnEnable() { Volumes[gameObject.scene.handle] = this; }
        void OnDisable()
        {
            int key = gameObject.scene.handle;
            if (Volumes.TryGetValue(key, out var volume) && volume == this) Volumes.Remove(key);
        }

        public static bool TryGet(Camera camera, out Material result)
        {
            result = null;
            Scene scene = camera.cameraType == CameraType.SceneView ? SceneManager.GetActiveScene() : camera.gameObject.scene;
            if (!Volumes.TryGetValue(scene.handle, out var volume) || volume == null || !volume.isActiveAndEnabled) return false;
            result = volume.material;
            return result != null;
        }
    }
}
