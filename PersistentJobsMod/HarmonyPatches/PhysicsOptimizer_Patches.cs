using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches
{
    /// <summary>
    /// Patches base game physics scripts to instantly return if the parent TrainCar is currently sleeping/optimized.
    /// This saves massive CPU time for distant frozen cars.
    /// </summary>
    [HarmonyPatch(typeof(Bogie), "FixedUpdate")]
    public static class Bogie_FixedUpdate_Patch
    {
        public static bool Prefix(Bogie __instance)
        {
            // If the train car's rigidbody is sleeping (which CarPerformanceOptimizer forces),
            // instantly cancel the C# script FixedUpdate to save CPU.
            if (__instance.Car != null && __instance.Car.rb != null && __instance.Car.rb.IsSleeping())
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Natively intercepts Brakeset.TickAll to skip fluid dynamic physics calculations
    /// for train consists that have been put to sleep by the CarPerformanceOptimizer.
    /// This completely eliminates the 4ms overhead with zero time-slicing delays.
    /// </summary>
    [HarmonyPatch(typeof(DV.Simulation.Brake.Brakeset), "TickAll")]
    public static class Brakeset_TickAll_Patch
    {
        public static bool Prefix(float dt)
        {
            foreach (var allSet in DV.Simulation.Brake.Brakeset.allSets)
            {
                if (allSet.firstCar != null)
                {
                    TrainCar car = allSet.firstCar.GetComponent<TrainCar>();
                    
                    // If the first car's Rigidbody is sleeping, the entire consist is asleep.
                    // We can safely skip the air flow simulation for this frozen train.
                    if (car != null && car.rb != null && car.rb.IsSleeping())
                    {
                        continue;
                    }
                }
                
                // Only tick active consists
                allSet.Tick(dt);
            }
            
            // Return false to skip the base method since we executed its logic
            return false;
        }
    }
}
