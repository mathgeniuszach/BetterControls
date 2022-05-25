using System.Collections.Generic;
using System.Reflection.Emit;

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DysonSphereProgram.Modding.BetterControls
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("DSPGAME.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.zach.BetterControls";
        public const string NAME = "BetterControls";
        public const string VERSION = "0.1.0";

        private Harmony _harmony;
        internal static ManualLogSource Log;

        private void Awake() {
            Plugin.Log = Logger;
            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(BetterControls));
            Logger.LogInfo("BetterControls Awake() called");
        }

        private void OnDestroy() {
            Logger.LogInfo("BetterControls OnDestroy() called");
            _harmony?.UnpatchSelf();
            Plugin.Log = null;
        }
    }

    public static class KeyIds {
        public const int Jump = BuiltinKey.跳跃;
        public const int Takeoff = BuiltinKey.起飞;
        public const int ClosePanel0 = BuiltinKey.关闭面板0;
        public const int Inventory = BuiltinKey.打开物品清单;
        public const int ClosePanel1 = BuiltinKey.关闭面板1;
        public const int GameMenu = BuiltinKey.打开游戏菜单;
    }

    class BetterControls {
        static bool[] conflicts = null;
        static Color conflictColor = Color.red;
        static Dictionary<int, int> indexes = new Dictionary<int, int>();
        static Dictionary<int, int> rindexes = new Dictionary<int, int>();
        static Dictionary<int, int> groups = new Dictionary<int, int>();
        static Dictionary<int, int> linked = new Dictionary<int, int>();

        static CombineKey getKey(CombineKey builtin, CombineKey overrider) {
            return overrider.IsNull() ? builtin : overrider;
        }

        public static bool inventoryKeyDown() {
            // Function to detect whether or not either of the inventory buttons are held down
            if (!VFInput.override_keys[KeyIds.Inventory].IsNull()) {
                return VFInput.override_keys[KeyIds.Inventory].GetKeyDown();
            }
            return VFInput.noModifier && Input.GetKeyDown(KeyCode.E);
        }

        public static bool menuKeyDown() {
            // Function to detect whether or not the escape menu key is down
            if (!VFInput.override_keys[KeyIds.GameMenu].IsNull()) {
                return VFInput.override_keys[KeyIds.GameMenu].GetKeyDown();
            }
            return VFInput.noModifier && Input.GetKeyDown(KeyCode.Escape);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(UIGame), nameof(UIGame._OnUpdate))]
        static IEnumerable<CodeInstruction> PatchInventoryKey(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            // Un-hardcode inventory button
            return new CodeMatcher(instructions, generator)
                .MatchForward(false,
                    new CodeMatch(ci => ci.LoadsConstant(KeyCode.E)),
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new System.Type[] {typeof(KeyCode)})))
                .RemoveInstruction()
                .SetOperandAndAdvance(AccessTools.Method(typeof(BetterControls), nameof(BetterControls.inventoryKeyDown)))
                .InstructionEnumeration();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VFInput), nameof(VFInput.OnUpdate))]
        static void OnUpdate() {
            // Un-hardcode escape button
            VFInput.escape = menuKeyDown() && !VFInput.inputing;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIOptionWindow), nameof(UIOptionWindow._OnOpen))]
        static void _OnOpen(ref UIOptionWindow __instance) {
            // Collect CombineKeys
            BuiltinKey[] builtinKeys = DSPGame.key.builtinKeys;
            CombineKey[] overrideKeys = __instance.tempOption.overrideKeys;
            int len = builtinKeys.Length;

            CombineKey[] keys = new CombineKey[len];
            for (int i = 0; i < len; i++) {
                keys[i] = getKey(builtinKeys[i].key, overrideKeys[builtinKeys[i].id]);
            }

            // Build extra conflict groups based on the default keys that conflict
            for (int i = 0; i < len-1; i++) {
                // Get the key for this i val
                CombineKey ikey = builtinKeys[i].key;

                // Skip over already conflicting keys
                if (!groups.ContainsKey(i)) {
                    groups[i] = -1;
                    // Loop over only keys that have not been checked yet
                    for (int ci = i+1; ci < len; ci++) {
                        // Get the key for this ci val
                        CombineKey cikey = builtinKeys[ci].key;

                        if (
                            ikey.IsEquals(cikey.keyCode, cikey.modifier, cikey.noneKey)
                        ) {
                            groups[i] = i;
                            groups[ci] = i;
                        }
                    }
                }
            }
            // Fill in the last key.
            if (!groups.ContainsKey(len-1)) groups[len-1] = -1;

            // Load conflicts array
            conflicts = new bool[len];

            // Initialize conflicts array (no need to check the last value)
            for (int i = 0; i < len-1; i++) {
                // Get the key for this i val
                CombineKey ikey = keys[i];

                // Skip any already marked conflicts
                if (!conflicts[i]) {
                    // Loop over only keys that have not been checked yet
                    for (int ci = i+1; ci < len; ci++) {
                        // Get the key for this ci val
                        CombineKey cikey = keys[ci];

                        if (
                            (builtinKeys[i].conflictKeyGroup & builtinKeys[ci].conflictKeyGroup) > 0 &&
                            (groups[i] != groups[ci] || groups[i] < 0) &&
                            ikey.IsEquals(cikey.keyCode, cikey.modifier, cikey.noneKey)
                        ) {
                            conflicts[i] = true;
                            conflicts[ci] = true;
                        }
                    }
                }
            }

            // Link keys
            int[] links = new int[] {
                indexes[KeyIds.Takeoff],        indexes[KeyIds.Jump],
                indexes[KeyIds.ClosePanel0],    indexes[KeyIds.Inventory],
                indexes[KeyIds.ClosePanel1],    indexes[KeyIds.GameMenu]
            };

            for (int i = 0; i < links.Length; i += 2) {
                // Link in dictionary
                linked[links[i]] = links[i+1];
                linked[links[i+1]] = links[i];
                
                // Ensure that keys are the same
                CombineKey k = keys[links[i+1]];
                if (!keys[links[i]].IsEquals(k.keyCode, k.modifier, k.noneKey)) {
                    __instance.keyEntries[links[i]].OverrideKey(__instance.tempOption, k.keyCode, k.modifier, k.noneKey);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuiltinKey), nameof(BuiltinKey.IsKeyVaild))]
        static bool IsKeyValid(ref bool __result) {
            __result = true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIKeyEntry), nameof(UIKeyEntry.SetEntry))]
        static void SetEntry(ref UIKeyEntry __instance, int _index, BuiltinKey _builtinKey, UIOptionWindow _optionWin) {
            // Show all keybinding editor UIs.
            if (_builtinKey.id < 71 || 78 < _builtinKey.id) { // We skip strongly hardcoded controls
                __instance.setTheKeyInput.gameObject.SetActive(true);
                __instance.setTheKeyToggle.gameObject.SetActive(true);
                __instance.setDefaultUIButton.gameObject.SetActive(true);
                __instance.setNoneKeyUIButton.gameObject.SetActive(true);
            }

            // Add pair to dictionary
            indexes[_builtinKey.id] = _index;
            rindexes[_index] = _builtinKey.id;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIKeyEntry), nameof(UIKeyEntry.Update))]
        static void Update(ref UIKeyEntry __instance) {
            // If conflicting with another key, update color
            if (conflicts[indexes[__instance.builtinKey.id]]) {
                __instance.keyText.color = Color.red;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIKeyEntry), nameof(UIKeyEntry.OnSetNoneKeyClick))]
        static bool OnSetNoneKeyClick(ref UIKeyEntry __instance) {
            // Make clearing a keybind use OverrideKey
            __instance.OverrideKey(__instance.optionWin.GetTempOption(), 0, 0, true);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIKeyEntry), nameof(UIKeyEntry.OverrideKey))]
        static bool OverrideKey(ref UIKeyEntry __instance, GameOption tempOption, int keyCode, byte modifier, bool noneKey) {
            if (__instance.inputUIButton._isPointerEnter && Input.GetKeyDown(KeyCode.Mouse0)) {
                __instance.nextNotOn = true;
            }

            int id = __instance.builtinKey.id;
            int ki = indexes[id];

            // Collect CombineKeys
            CombineKey thiskey = __instance.builtinKey.key;
            BuiltinKey[] builtinKeys = DSPGame.key.builtinKeys;
            CombineKey[] overrideKeys = tempOption.overrideKeys;
            int len = builtinKeys.Length;

            CombineKey[] keys = new CombineKey[len];
            for (int i = 0; i < len; i++) {
                keys[i] = getKey(builtinKeys[i].key, overrideKeys[rindexes[i]]);
            }

            // Get old key
            int oldKeyCode = keys[ki].keyCode;
            byte oldModifier = keys[ki].modifier;
            bool oldNoneKey = keys[ki].noneKey;
            // Get new key
            int newKeyCode = keyCode;
            byte newModifier = modifier;
            bool newNoneKey = noneKey;
            // If new key is null, default to builtin key
            if (keyCode == 0 && modifier == 0 && !noneKey) {
                newKeyCode = thiskey.keyCode;
                newModifier = thiskey.modifier;
                newNoneKey = thiskey.noneKey;
            }

            // If the key isn't changing, don't change anything 
            if (oldKeyCode == newKeyCode && oldModifier == newModifier && oldNoneKey == newNoneKey) {
                __instance.inputUIButton.highlighted = false;
                return false;
            }

            // Always update the keycode
            if (thiskey.IsEquals(newKeyCode, newModifier, newNoneKey)) {
                // This key is the default
                overrideKeys[id].Reset();
            } else {
                // This key is not the default
                overrideKeys[id].keyCode = newKeyCode;
                overrideKeys[id].modifier = newModifier;
                overrideKeys[id].noneKey = newNoneKey;
            }

            __instance.optionWin.SetTempOption(tempOption);
            __instance.inputUIButton.highlighted = false;

            // Calculate any keys with conflicts before
            int c = -1;
            for (int i = 0; i < len; i++) {
                if (
                    i != ki && conflicts[i] &&
                    (builtinKeys[i].conflictKeyGroup & builtinKeys[ki].conflictKeyGroup) > 0 &&
                    (groups[i] != groups[ki] || groups[i] < 0) &&
                    keys[i].IsEquals(oldKeyCode, oldModifier, oldNoneKey)
                ) {
                    if (c > -1 && !(linked.ContainsKey(c) && linked[c] == i)) { // Skip linked keys
                        // At least two other keys are conflicting, no updates needed
                        c = -1;
                        break;
                    }
                    // Save this key for later
                    c = i;
                }
            }

            // If there is just one key that used to conflict with this key, update it
            if (c > -1) {
                conflicts[c] = false;

                // Unmark linked key
                if (linked.ContainsKey(c)) conflicts[linked[c]] = false;
            }
            conflicts[ki] = false;

            // Calculate any keys with conflicts after
            for (int i = 0; i < len; i++) {
                if (
                    i != ki &&
                    (builtinKeys[i].conflictKeyGroup & builtinKeys[ki].conflictKeyGroup) > 0 &&
                    (groups[i] != groups[ki] || groups[i] < 0) &&
                    keys[i].IsEquals(newKeyCode, newModifier, newNoneKey)
                ) {
                    // Key conflicts, update that key to show conflict
                    conflicts[i] = true;
                    conflicts[ki] = true;

                    // Mark linked key
                    if (linked.ContainsKey(i)) conflicts[linked[c]] = true;

                    __instance.StartConflictText(i);
                    __instance.setTheKeyToggle.isOn = false;
                    __instance.waitingText.gameObject.SetActive(false);
                    // We can break early here, because other keys are already marked as conflicting.
                    break;
                }
            }

            // Link key with another. They change each other
            if (linked.ContainsKey(ki)) {
                __instance.optionWin.keyEntries[linked[ki]].OverrideKey(tempOption, newKeyCode, newModifier, newNoneKey);
            }

            return false;
        }
    }
}
