using HarmonyLib;
using MelonLoader;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(Main.Core), "Waypoint + Minimap", "1.0.0", "TheThinker")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace Main
{
    public class Core : MelonMod
    {
        private readonly MinimapMod minimap = new MinimapMod();

        public override void OnInitializeMelon()
        {
            WaypointManager.Init();
            minimap.Init();

            LoggerInstance.Msg("Waypoint + Minimap loaded successfully.");
            LoggerInstance.Msg("B = place waypoint, N = delete last, Shift+N = clear all, M = toggle minimap.");

            HarmonyInstance.PatchAll();
        }

        public override void OnUpdate()
        {
            WaypointManager.Update();
            minimap.OnUpdate();

            if (InputHelper.GetKeyDown(Key.B))
            {
                WaypointManager.PlaceWaypoint();
            }

            if (InputHelper.GetKeyDown(Key.N))
            {
                WaypointManager.DeleteLastWaypoint();
            }

            if (InputHelper.GetKey(Key.LeftShift) &&
                InputHelper.GetKeyDown(Key.N))
            {
                WaypointManager.ClearWaypoints();
            }
        }

        public override void OnGUI()
        {
            minimap.OnGUI();
        }

        public override void OnDeinitializeMelon()
        {
            minimap.OnDeinitializeMelon();
        }
    }
}
