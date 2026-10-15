using System;
using System.Collections.Generic;
using System.Collections;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace ValheimWhiteboard
{
    internal static class RuntimeCompatibilityTests
    {
        public static void Run(Action<bool,string> check)
        {
            var plugin = (BepInPlugin)Attribute.GetCustomAttribute(typeof(WhiteboardPlugin),typeof(BepInPlugin));
            var policy = (NetworkCompatibilityAttribute)Attribute.GetCustomAttribute(typeof(WhiteboardPlugin),typeof(NetworkCompatibilityAttribute));
            // Jotunn's compatibility DTOs are internal; invoke its actual implementation
            // through reflection rather than duplicating its policy logic in test code.
            var assembly = typeof(Jotunn.Main).Assembly;
            var moduleType = assembly.GetType("Jotunn.Utils.ModModule",true);
            var dataType = assembly.GetType("Jotunn.Utils.ModuleVersionData",true);
            var listType = typeof(List<>).MakeGenericType(moduleType);
            var module = AccessTools.Constructor(moduleType,new[]{typeof(BepInPlugin),typeof(NetworkCompatibilityAttribute)}).Invoke(new object[]{plugin,policy});
            bool Needed(string name) => (bool)AccessTools.Method(moduleType,name).Invoke(module,null);
            check(Needed("IsNeededOnServer") && !Needed("IsNeededOnClient"), "actual Jotunn module declares host required and client optional");
            object Data(params object[] modules)
            { var list = (IList)Activator.CreateInstance(listType); foreach (var item in modules) list.Add(item); return AccessTools.Constructor(dataType,new[]{listType}).Invoke(new object[]{list}); }
            var compare = AccessTools.Method(assembly.GetType("Jotunn.Utils.ModCompatibility",true), "CompareVersionData");
            bool Compatible(object server,object client) => (bool)compare.Invoke(null,new object[]{server,client});
            object Version(string version) => AccessTools.Constructor(moduleType,new[]{typeof(string),typeof(string),typeof(System.Version),typeof(CompatibilityLevel),typeof(VersionStrictness)})
                .Invoke(new object[]{plugin.GUID,plugin.Name,new System.Version(version),CompatibilityLevel.ServerMustHaveMod,VersionStrictness.Minor});
            var serverData = Data(module); var stock = Data();
            check(Compatible(serverData,stock), "actual Jotunn version comparison allows absent client chalkboard module");
            check(!Compatible(stock,serverData), "actual Jotunn version comparison rejects missing host chalkboard module");
            check(!Compatible(serverData,Data(Version("0.4.1"))), "actual Jotunn version comparison rejects old custom-prefab clients");
            check(!Compatible(serverData,Data(Version("0.5.0"))), "actual Jotunn version comparison rejects older snapshot protocol clients");
            check(Compatible(serverData,Data(Version("0.6.1"))), "actual Jotunn minor policy allows compatible patch versions");
            var package = (ZPackage)AccessTools.Method(dataType,"ToZPackage").Invoke(serverData,null); package.SetPos(0);
            var copy = AccessTools.Constructor(dataType,new[]{typeof(ZPackage)}).Invoke(new object[]{package});
            check(Compatible(serverData,copy), "actual Jotunn version package roundtrip preserves compatibility");
        }
    }
}
