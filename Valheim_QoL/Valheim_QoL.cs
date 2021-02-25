using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;


public static class Utils
{
    public static void Log(object obj)
    {
        FileLog.Log(obj.ToString());
        FileLog.FlushBuffer();
    }

    public static void LogInstanceData(object __instance)
    {
        BindingFlags bindingFlags = BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance |
                            BindingFlags.Static;
        Log($"\n======== {__instance.GetType()} =========");
        foreach (FieldInfo field in __instance.GetType().GetFields(bindingFlags))
        {
            Log($"{field.Name} -> {field.GetValue(__instance)}");
        }
    }
        
}

namespace Valheim_QoL
{

    public class Valheim_QoL
    {

        public static void Main(string[] args)
        {
            new Thread(delegate ()
            {
                var harmony = new Harmony("com.valheim.qol");
                //Harmony.DEBUG = true;
                FileLog.Reset();

                bool flag;
                do
                {
                    flag = AccessTools.Method(typeof(global::Console), "InputText", null, null) != null
                    && AccessTools.Method(typeof(global::Console), "Print", null, null) != null;
                    Thread.Sleep(1000);
                }
                while (!flag);
                harmony.PatchAll();
                Utils.Log("Patched functions!");
            }).Start();
        }
    }


    // Interact while holding tool
    [HarmonyPatch(typeof(Player), "UpdateHover")]
    public static class InteractionPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var index = -1;
            for (var i = 0; i < codes.Count; i++)
            {
                // Look for IL we want to remove
                // IL_0001: callvirt instance bool Character::InPlaceMode()
                var instruction = codes[i].ToString();
                if (instruction.Contains("InPlaceMode"))
                {
                    Utils.Log("Found place mode instruction");
                    index = i;
                    break;
                }
            }
            
            if (index > -1)
            {
                // remove the 3 instructions so we dont call the function in the if statement
                // if (this.InPlaceMode() <- REMOVE THIS || this.IsDead() || this.m_shipControl != null)
                codes.RemoveRange(index - 1, 3);
                Utils.Log("removed place mode instruction");
            }

            return codes.AsEnumerable();
        }
    }

    
    // No stamina to repair
    [HarmonyPatch(typeof(Player), "Repair")]
    public static class RepairStamina
    {
        static ItemDrop.ItemData __item = null;
        public static bool Prefix(Player __instance, ref ItemDrop.ItemData toolItem, Piece repairPiece)
        {
            // Use 0 stamina when repairing
            toolItem.m_shared.m_attack.m_attackStamina = 0f;
            __item = toolItem;
            return true;
        }
        public static void Postfix()
        {
            __item.m_shared.m_attack.m_attackStamina = 20f;
        }
    }

    // Log class variables for useful info as well as stack data to trace 
    // function calls
    //[HarmonyPatch(typeof(Player), "UseStamina")]
    public static class LogInstances
    {
        static StackTrace stack = new StackTrace();
        public static bool Prefix(object __instance)
        {
            foreach (StackFrame frame in stack.GetFrames())
            {
                Utils.Log(frame.GetMethod().Name);
            }
            Utils.LogInstanceData(__instance);
            return true;
        }
    }

    // No stamina when hoe'ing
    [HarmonyPatch(typeof(Player), "UseStamina")]
    public static class HoeStamina
    {

        public static bool Prefix(Player __instance, ref float v)
        {
            var __item = __instance.GetRightItem();
            if (__item.m_dropPrefab.ToString().Contains("Hoe"))
            {
                v = 0f;
            }
            return true;
        }

    }





    [HarmonyPatch(typeof(global::Console), "InputText")]
    public static class InputText_Patch
    {
        // Token: 0x06000003 RID: 3 RVA: 0x00002084 File Offset: 0x00000284
        public static bool Prefix(global::Console __instance)
        {
            string text = global::Console.instance.m_input.text;
            string[] array = text.Split(new char[]
            {
                ' '
            });
            if (text.Equals("syncmap"))
            {
                InputText_Patch.PrintOut("\n _SYNC MAP IS ACTIVE_ \n");
            }
            return true;
        }


        public static void PrintOut(string log)
        {
            global::Console instance = global::Console.instance;
            if (instance == null)
            {
                return;
            }
            instance.Print(log);
        }
    }
}
