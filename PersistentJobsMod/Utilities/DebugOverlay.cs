using DV.Simulation.Cars;
using DV.Utils;
using PersistentJobsMod.Spawning;
using UnityEngine;

namespace PersistentJobsMod.Utilities
{
    public class DebugOverlay : SingletonBehaviour<DebugOverlay>
    {
        private bool _showOverlay = false;
        private float _deltaTime = 0.0f;

        public static void Create()
        {
            if (Instance == null)
            {
                var go = new GameObject("[PersistentJobsMod_DebugOverlay]");
                go.AddComponent<DebugOverlay>();
                DontDestroyOnLoad(go);
            }
        }

        public static void Shutdown()
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }
        }

        private void Update()
        {
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;

            if (Input.GetKeyDown(KeyCode.F11))
            {
                _showOverlay = !_showOverlay;
            }
        }

        private void OnGUI()
        {
            if (!_showOverlay) return;

            int totalCars = 0;
            int sleepingCars = 0;

            if (CarSpawner.Instance != null && CarSpawner.Instance.AllCars != null)
            {
                totalCars = CarSpawner.Instance.AllCars.Count;
                sleepingCars = CarPerformanceOptimizer.Instance != null ? CarPerformanceOptimizer.Instance.SleepingCarCount : 0;
            }

            float fps = 1.0f / _deltaTime;
            string text = $"Persistent Jobs Debug\n" +
                          $"FPS: {fps:0.}\n" +
                          $"Total Cars: {totalCars}\n" +
                          $"Active Cars (Physics On): {totalCars - sleepingCars}\n" +
                          $"Sleeping Cars (Optimized): {sleepingCars}";

            GUIStyle style = new GUIStyle();
            int w = Screen.width, h = Screen.height;
            Rect rect = new Rect(20, 20, w, h * 2 / 100);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = 24;
            style.normal.textColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            
            // Draw a background box for readability
            GUI.Box(new Rect(10, 10, 400, 150), "");
            GUI.Label(rect, text, style);
        }
    }
}
