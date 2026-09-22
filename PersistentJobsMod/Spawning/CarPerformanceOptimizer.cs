using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using DV;
using DV.Utils;
using UnityEngine;

namespace PersistentJobsMod.Spawning
{
    public class CarPerformanceOptimizer : SingletonBehaviour<CarPerformanceOptimizer>
    {
        private const float CHECK_INTERVAL = 5.0f;

        // 2000 meters squared = 4,000,000. The player can't see or interact with cars this far away.
        private const float ACTIVE_DISTANCE_SQR = 4000000f;

        // 600 meters squared = 360,000. Loose/unused cars fall asleep faster.
        private const float INACTIVE_DISTANCE_SQR = 360000f;
        public static void Create()
        {
            if (Instance == null)
            {
                GameObject go = new GameObject("[PersistentJobsMod_CarPerformanceOptimizer]");
                go.AddComponent<CarPerformanceOptimizer>();
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

        private void OnEnable()
        {
            StartCoroutine(OptimizationLoop());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private HashSet<TrainCar> optimizedCars = new HashSet<TrainCar>();
        private HashSet<Trainset> _processedTrainsets = new HashSet<Trainset>();
        private Dictionary<TrainCar, Renderer[]> _rendererCache = new Dictionary<TrainCar, Renderer[]>();
        private bool _hasRunOnce = false;

        // Pre-allocated list to avoid LINQ/delegate allocations during cleanup
        private List<TrainCar> _carsToRemove = new List<TrainCar>();

        public int SleepingCarCount => optimizedCars.Count;

        private void SetRenderersEnabled(TrainCar trainCar, bool enabled)
        {
            if (!_rendererCache.TryGetValue(trainCar, out Renderer[] renderers))
            {
                renderers = trainCar.GetComponentsInChildren<Renderer>(true);
                _rendererCache[trainCar] = renderers;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        private IEnumerator OptimizationLoop()
        {
            Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

            while (true)
            {
                yield return new WaitForSecondsRealtime(CHECK_INTERVAL);
                if (Main.Stop || Main.Pause) continue;
                if (PlayerManager.PlayerTransform == null || FastTravelController.IsFastTravelling) continue;
                if (CarSpawner.Instance == null || CarSpawner.Instance.AllCars == null) continue;

                stopwatch.Restart();
                int stateChangesThisFrame = 0;

                Vector3 playerPos = PlayerManager.PlayerTransform.position;

                int carsOptimizedThisTick = 0;
                int carsWokenThisTick = 0;

                // Cleanup: remove destroyed cars and their cached renderer arrays without LINQ
                _carsToRemove.Clear();
                foreach (TrainCar c in optimizedCars)
                {
                    if (c == null || c.gameObject == null)
                    {
                        _rendererCache.Remove(c);
                        _carsToRemove.Add(c);
                    }
                }
                foreach (TrainCar c in _carsToRemove)
                {
                    optimizedCars.Remove(c);
                }
                _carsToRemove.Clear();

                _processedTrainsets.Clear();

                // Iterate using an index-based for loop. This prevents exceptions if 
                // the AllCars collection is modified while we yield across frames.
                for (int i = 0; i < CarSpawner.Instance.AllCars.Count; i++)
                {
                    TrainCar trainCar = CarSpawner.Instance.AllCars[i];
                    if (trainCar == null || trainCar.gameObject == null) continue;

                    Trainset trainset = trainCar.trainset;
                    if (trainset == null || !_processedTrainsets.Add(trainset)) continue;

                    bool shouldBeOptimized = true;
                    bool consistHasPlayerSpawnedCar = false;
                    bool consistHasActiveJobOrLoco = false;

                    // First pass: check trainset properties
                    foreach (TrainCar car in trainset.cars)
                    {
                        if (car == null) continue;
                        if (car.playerSpawnedCar) consistHasPlayerSpawnedCar = true;
                        if (car.IsLoco) consistHasActiveJobOrLoco = true;
                        
                        if (car.logicCar != null)
                        {
                            // Passing 'true' here is CRITICAL.
                            // Otherwise, it loops over EVERY single generated job in the entire game (thousands) 
                            // to find this car's job, taking massive amounts of CPU for large trainsets.
                            // Since we only care if it's InProgress anyway, we only need to search active jobs.
                            var job = SingletonBehaviour<DV.Logic.Job.JobsManager>.Instance.GetJobOfCar(car.logicCar, true);
                            if (job != null && job.State == DV.ThingTypes.JobState.InProgress)
                            {
                                consistHasActiveJobOrLoco = true;
                            }
                        }
                    }

                    float requiredSqrDistance = consistHasActiveJobOrLoco ? ACTIVE_DISTANCE_SQR : INACTIVE_DISTANCE_SQR;

                    // Evaluate distance. Short-circuit as soon as we find a car close to the player.
                    foreach (TrainCar car in trainset.cars)
                    {
                        if (car == null) continue;

                        float d = (car.transform.position - playerPos).sqrMagnitude;
                        if (d <= requiredSqrDistance)
                        {
                            shouldBeOptimized = false;
                            break;
                        }
                    }

                    // Never sleep a consist that contains the player's own car
                    if (shouldBeOptimized && consistHasPlayerSpawnedCar)
                    {
                        shouldBeOptimized = false;
                    }

                    int stateChangesThisTrainset = 0;

                    // Process all cars in the consist atomically
                    foreach (TrainCar car in trainset.cars)
                    {
                        if (car == null || car.gameObject == null) continue;

                        bool isCurrentlyOptimized = optimizedCars.Contains(car);
                        if (isCurrentlyOptimized == shouldBeOptimized) continue;

                        if (shouldBeOptimized)
                        {
                            car.ForceOptimizationState(true);
                            SetRenderersEnabled(car, false);
                            optimizedCars.Add(car);
                            carsOptimizedThisTick++;
                        }
                        else
                        {
                            SetRenderersEnabled(car, true);
                            car.ForceOptimizationState(false);
                            optimizedCars.Remove(car);
                            carsWokenThisTick++;
                        }
                        stateChangesThisTrainset++;
                    }

                    stateChangesThisFrame += stateChangesThisTrainset;

                    // Yield if we've processed 20+ state changes, OR spent more than 2ms working this frame.
                    // We ignore the budget on the very first run (_hasRunOnce == false) 
                    // so all distant cars are immediately put to sleep after loading.
                    if (_hasRunOnce && (stateChangesThisFrame >= 20 || stopwatch.ElapsedMilliseconds >= 2))
                    {
                        if (stateChangesThisFrame > 0)
                        {
                            Main._modEntry.Logger.Log($"[CarOptimizer] Frame yield. Processed {stateChangesThisFrame} car state changes in this batch ({stopwatch.ElapsedMilliseconds}ms elapsed).");
                        }
                        yield return null;
                        stopwatch.Restart();
                        stateChangesThisFrame = 0;
                    }
                }

                _hasRunOnce = true;

                if (carsOptimizedThisTick > 0 || carsWokenThisTick > 0)
                {
                    Main._modEntry.Logger.Log($"[CarOptimizer] Slept {carsOptimizedThisTick} cars, Woke {carsWokenThisTick} cars. Total Active: {CarSpawner.Instance.AllCars.Count - optimizedCars.Count}, Total Sleeping: {optimizedCars.Count}");
                }
            }
        }
    }
}
