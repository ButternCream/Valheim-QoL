using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;


public static class ModUtils
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
                ModUtils.Log("Patched functions!");
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
                    ModUtils.Log("Found place mode instruction");
                    index = i;
                    break;
                }
            }
            
            if (index > -1)
            {
                // remove the 3 instructions so we dont call the function in the if statement
                // if (this.InPlaceMode() <- REMOVE THIS || this.IsDead() || this.m_shipControl != null)
                codes.RemoveRange(index - 1, 3);
                ModUtils.Log("removed place mode instruction");
            }

            return codes.AsEnumerable();
        }
    }

    // Log class variables for useful info as well as stack data to trace 
    // function calls
    //[HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    public static class LogInstances
    {
        static StackTrace stack = new StackTrace();
        public static bool Prefix(object __instance)
        {
            Player p = Player.m_localPlayer;
            ModUtils.LogInstanceData(p.GetInventory());
            foreach (StackFrame frame in stack.GetFrames())
            {
                ModUtils.Log(frame.GetMethod().Name);
            }
            ModUtils.Log("\n");
            ModUtils.LogInstanceData(__instance);
            return true;
        }
    }

    //No stamina when using the hoe, hammer or cultivator
    [HarmonyPatch(typeof(Player), "UseStamina")]
    public static class NoStamina_Patch
    {
        public static bool Prefix(Player __instance, ref float v)
        {
            if (__instance.GetRightItem() == null) return true;
            switch (__instance.GetRightItem().m_shared.m_name)
            {
                case "$item_hoe":
                case "$item_hammer":
                case "$item_cultivator":
                    v = 0f;
                    break;
                default:
                    break;
            }

            return true;
        }

    }
    
    //Draw sphere around 
    //TODO
    [HarmonyPatch(typeof(Plant), "Awake")]
    public static class DrawPlantSpace_Patch
    {
        public static bool Prefix(Plant __instance)
        {
            // Needs to be drawn in OnDrawGizmos()
            Gizmos.DrawWireCube(__instance.transform.position,
                new Vector3(__instance.m_growRadius, __instance.m_growRadius, __instance.m_growRadius));
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

    [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
    public static class ChatDataReceiver
    {
        private static List<string> ReceivedPackets = new List<string>();
        public static bool DataReadyForMerge = false;
        private static int FullDataMessageNumber = 4_194_304;
        private static int PacketNum = 1000;
        public static bool[] ExplorationData = new bool[FullDataMessageNumber];
        public static bool Prefix(ref GameObject go, long senderID, Vector3 pos, Talker.Type type, ref string user, ref string text)
        {
            if (senderID == Player.m_localPlayer.GetPlayerID())
            {
                // InputText_Patch.PrintOut("Not processing own message" + text);
                return true;
            }

            if (text.StartsWith("START_") && text.EndsWith("#END"))
            {
                ReceivedPackets.Add(text);
                if (ReceivedPackets.Count == PacketNum)
                {
                    InputText_Patch.PrintOut("1/4 Merging exploration data.");
                    AssembleMapData();
                    InputText_Patch.PrintOut("data parsed, exploration content: " + ExplorationData.Where(x => x).Count());
                    DataReadyForMerge = true;
                    InputText_Patch.PrintOut("2/4 Data reassembly done.");
                }
            }
            else
            {
            }
            return true;
        }

        public static void Clear()
        {
            ExplorationData = new bool[FullDataMessageNumber];
            ReceivedPackets.Clear();
        }

        private static void AssembleMapData()
        {
            int dataPerPackage = FullDataMessageNumber / PacketNum;
            int howMany, what, packageId;
            string[] dataArr, pairs, numXvalue;

            for (int i = 0; i < ReceivedPackets.Count; i++)
            {
                dataArr = ReceivedPackets[i].Split('#');           // START_0#101x0,99x1,1x0,49x1,251x0,299x1,3394x0#END  
                packageId = Int32.Parse(dataArr[0].Split('_')[1]);
                pairs = dataArr[1].Split(',');
                int index = packageId * dataPerPackage;
                for (int j = 0; j < pairs.Length; j++)
                {
                    numXvalue = pairs[j].Split('x');
                    howMany = Int32.Parse(numXvalue[0]);
                    what = Int32.Parse(numXvalue[1]);
                    for (int k = 0; k < howMany; k++)
                    {
                        ExplorationData[index] = what == 1;
                        index++;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "Update")]
    public static class SyncButtonAddListener
    {
        static float cooldownTimerBase = 10f;
        static float cooldownTimer = 10f;
        static float cooldownTimerResolutionBase = 2f;
        static float cooldownTimerResolution = 2f;

        public static bool Prefix(ref bool[] ___m_explored, ref Texture2D ___m_fogTexture, ref List<ZNet.PlayerInfo> ___m_tempPlayerInfo)
        {
            if (ChatDataReceiver.DataReadyForMerge)
            {
                ChatDataReceiver.DataReadyForMerge = false;
                int explored = ___m_explored.Where(x => x).Count();
                int friendExplord = ChatDataReceiver.ExplorationData.Where(x => x).Count();
                //  InputText_Patch.PrintOut("Explorated by me : " + explored + ", explored by friend: " + friendExplord);
                if (___m_explored.Length != ChatDataReceiver.ExplorationData.Length)
                {
                    InputText_Patch.PrintOut("Map data resolution error.");
                }
                else
                {
                    InputText_Patch.PrintOut("Your resoluations are the same: " + ___m_explored.Length);
                }

                int num1 = 2048, num2 = 2048;   ///only for me!!!!

                Color32[] array = new Color32[num1 * num2];
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
                }
                ___m_fogTexture.SetPixels32(array);
                ___m_fogTexture.Apply();

                for (int i = 0; i < ___m_explored.Length; i++)
                {
                    ___m_explored[i] = ___m_explored[i] || ChatDataReceiver.ExplorationData[i]; //merge

                    int x = i % num2;
                    int y = i / num2;

                    if (___m_explored[y * num2 + x])
                    {
                        ___m_fogTexture.SetPixel(x, y, new Color(0, 0, 0, 0));
                    }
                }
                InputText_Patch.PrintOut("3/4 Map data merge completed.");
                ___m_fogTexture.Apply();
                ___m_fogTexture.Apply(true, false);

                InputText_Patch.PrintOut("4/4 Map texture data refreshed");
                ChatDataReceiver.Clear();
            }

            if (cooldownTimer <= 0 && Input.GetKeyDown(KeyCode.F10))    ////1000
            {
                cooldownTimer = cooldownTimerBase;
                Chat c = Chat.instance;
                var packetNum = 1000;
                int dataPerPackage = ___m_explored.Length / packetNum;  //4194
                string dataPack = "";
                int limit = dataPerPackage;
                int remainder = ___m_explored.Length - dataPerPackage * packetNum;

                for (int i = 0; i <= packetNum; i++)
                {
                    dataPack = "START_" + i + "#";
                    int oneStreak = 0, zeroStreak = 0;
                    if (i == packetNum)
                    {
                        limit = remainder;
                    }
                    for (int j = 0; j < limit; j++)
                    {
                        if (___m_explored[i * dataPerPackage + j])
                        {
                            if (zeroStreak > 0)
                            {
                                dataPack += zeroStreak + "x0,";
                                zeroStreak = 0;
                            }
                            oneStreak++;
                        }
                        else
                        {
                            if (oneStreak > 0)
                            {
                                dataPack += oneStreak + "x1,";
                                oneStreak = 0;
                            }
                            zeroStreak++;
                        }
                    }

                    if (zeroStreak > 0)
                    {
                        dataPack += zeroStreak + "x0";
                        zeroStreak = 0;
                    }
                    if (oneStreak > 0)
                    {
                        dataPack += zeroStreak + "x1";
                        oneStreak = 0;
                    }

                    dataPack += "#END";
                    c.SendText(Talker.Type.Whisper, dataPack);
                }
            }

            if (cooldownTimerResolution <= 0 && Input.GetKeyDown(KeyCode.F11))    ////1000
            {
                cooldownTimerResolution = cooldownTimerResolutionBase;
                InputText_Patch.PrintOut("Resolution:" + ___m_explored.Length);
            }

            cooldownTimer -= Time.deltaTime;
            cooldownTimerResolution -= Time.deltaTime;
            return true;
        }

    }
}
