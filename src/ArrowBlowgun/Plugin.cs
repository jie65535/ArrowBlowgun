using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace ArrowBlowgun;

/// <summary>
/// The BepInEx plugin class of ArrowBlowgun.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.github.jie65535.ArrowBlowgun";
    internal const string PluginName = "ArrowBlowgun";
    internal const string PluginVersion = "0.1.1";

    private const string PrefabName = "ArrowBlowgun";
    private const string ItemNameKey = "ARROW_BLOWGUN";
    private const string ItemPrefabFolder = "0_Items/";
    private const int DefaultUses = 3;
    private const int MinimumUses = 1;
    private const float DefaultShotUseTime = 1f;
    private const float MinimumShotUseTime = 0f;
    private const float MaximumShotUseTime = 10f;
    private const float DefaultAimCorrectionDegrees = 35f;
    private const float MinimumAimCorrectionDegrees = 0f;
    private const float MaximumAimCorrectionDegrees = 90f;
    private const bool DefaultAddToLootPool = true;

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Item? RegisteredItem { get; private set; }
    internal int ConfiguredUses => usesConfig.Value;
    internal bool ConfiguredAddToLootPool => addToLootPoolConfig.Value;

    private ConfigEntry<int> usesConfig = null!;
    private ConfigEntry<float> shotUseTimeConfig = null!;
    private ConfigEntry<float> aimCorrectionDegreesConfig = null!;
    private ConfigEntry<bool> addToLootPoolConfig = null!;
    private int effectiveUses = DefaultUses;
    private bool effectiveAddToLootPool = DefaultAddToLootPool;
    private SpawnPool originalLootSpawnLocations = SpawnPool.None;
    private GameObject prefabContainer = null!;

    private void Awake()
    {
        Log = Logger;
        usesConfig = Config.Bind(
            "Balance",
            "Uses",
            DefaultUses,
            "Number of shots before the Arrow Blowgun is consumed. Minimum: 1. "
                + "In multiplayer, the room creator's value is used for everyone."
        );
        shotUseTimeConfig = Config.Bind(
            "Balance",
            "ShotUseTime",
            DefaultShotUseTime,
            "Seconds the primary action must be held before firing. Set to 0 for an "
                + "instant semi-automatic shot. This is a per-player setting and is not "
                + $"synchronized. Range: {MinimumShotUseTime}-{MaximumShotUseTime}."
        );
        aimCorrectionDegreesConfig = Config.Bind(
            "Balance",
            "AimCorrectionDegrees",
            DefaultAimCorrectionDegrees,
            "Maximum angle in degrees that a shot may turn from the muzzle toward the "
                + "center-screen aim point. Set to 0 to disable correction. This is a "
                + $"per-player setting and is not synchronized. Range: {MinimumAimCorrectionDegrees}-{MaximumAimCorrectionDegrees}."
        );
        addToLootPoolConfig = Config.Bind(
            "Spawning",
            "AddToLootPool",
            DefaultAddToLootPool,
            "Whether the Arrow Blowgun is included in the vanilla blowgun's random loot "
                + "pools. In multiplayer, the room creator's value is used for everyone."
        );

        if (usesConfig.Value < MinimumUses)
        {
            Log.LogWarning(
                $"Configured Uses value {usesConfig.Value} is invalid; using {MinimumUses}."
            );
            usesConfig.Value = MinimumUses;
        }

        ValidateFloatConfig(
            shotUseTimeConfig,
            DefaultShotUseTime,
            MinimumShotUseTime,
            MaximumShotUseTime
        );
        ValidateFloatConfig(
            aimCorrectionDegreesConfig,
            DefaultAimCorrectionDegrees,
            MinimumAimCorrectionDegrees,
            MaximumAimCorrectionDegrees
        );

        effectiveUses = usesConfig.Value;
        effectiveAddToLootPool = addToLootPoolConfig.Value;
        gameObject.AddComponent<RoomConfigSynchronizer>().Initialize(this);

        prefabContainer = new GameObject($"{PluginGuid}.Prefabs");
        prefabContainer.SetActive(false);
        DontDestroyOnLoad(prefabContainer);

        StartCoroutine(ArrowTrapFeedback.Initialize());
        StartCoroutine(RegisterArrowBlowgun());
        StartCoroutine(WarmUpArrowVisual());

        Log.LogInfo($"Plugin {PluginName} is loaded!");
    }

    private static IEnumerator WarmUpArrowVisual()
    {
        while (Character.localCharacter == null)
        {
            yield return null;
        }

        // CharacterAfflictions builds its reusable arrow pool during Awake.
        yield return null;
        ArrowVisualFactory.WarmUp();

        Item? registeredItem = RegisteredItem;
        if (registeredItem != null)
        {
            int attachedCount = ArrowVisualFactory.AttachLoadedArrowVisuals(registeredItem);
            Log.LogInfo($"Attached loaded arrow visuals to {attachedCount} Arrow Blowgun objects.");
        }
    }

    private IEnumerator RegisterArrowBlowgun()
    {
        // Let other plugins finish replacing Photon services before wrapping the active pool.
        yield return null;
        yield return null;

        ItemDatabase database = SingletonAsset<ItemDatabase>.Instance;
        if (database == null)
        {
            Log.LogError("ItemDatabase could not be loaded.");
            yield break;
        }

        Item? sourceItem = database.Objects.FirstOrDefault(item =>
            item != null && item.GetComponent<Action_RaycastDart>() != null
        );

        if (sourceItem == null)
        {
            Log.LogError("Could not find the vanilla blowgun item with Action_RaycastDart.");
            yield break;
        }

        GameObject prefab = Instantiate(sourceItem.gameObject, prefabContainer.transform);
        prefab.name = PrefabName;

        Item item = prefab.GetComponent<Item>();
        Action_RaycastDart vanillaAction = prefab.GetComponent<Action_RaycastDart>();
        if (item == null || vanillaAction == null)
        {
            Log.LogError("The cloned blowgun is missing Item or Action_RaycastDart.");
            Destroy(prefab);
            yield break;
        }

        vanillaAction.enabled = false;

        Action_FireArrow arrowAction = prefab.AddComponent<Action_FireArrow>();
        arrowAction.CopyFrom(vanillaAction, aimCorrectionDegreesConfig.Value);

        item.UIData.itemName = ItemNameKey;
        item.UIData.isShootable = true;
        item.UIData.hideFuel = effectiveUses <= MinimumUses;
        item.usingTimePrimary = shotUseTimeConfig.Value;
        item.totalUses = effectiveUses;

        LootData? lootData = item.GetComponent<LootData>();
        if (lootData != null)
        {
            originalLootSpawnLocations = lootData.spawnLocations;
            ApplyLootPoolSetting(item);
        }
        else
        {
            Log.LogWarning("The cloned Arrow Blowgun has no LootData component.");
        }

        RegisterItemName();

        RegisterItem(database, item);

        // A cloned LootData component will be discovered the next time loot weights are built.
        LootData.AllSpawnWeightData = null;
        RegisteredItem = item;

        Log.LogInfo(
            $"Registered {item.gameObject.name} from vanilla source {sourceItem.gameObject.name} "
                + $"with item ID {item.itemID}, {item.totalUses} uses, "
                + $"{item.usingTimePrimary:0.###} second use time, and "
                + $"{aimCorrectionDegreesConfig.Value:0.###} degree aim correction."
        );
    }

    internal void ApplyEffectiveUses(int uses, string source)
    {
        if (uses < MinimumUses)
        {
            Log.LogWarning($"Ignoring invalid synchronized Uses value {uses} from {source}.");
            return;
        }

        bool changed = effectiveUses != uses;
        effectiveUses = uses;

        Item? registeredItem = RegisteredItem;
        if (registeredItem != null)
        {
            foreach (Item item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item == null || item.itemID != registeredItem.itemID)
                {
                    continue;
                }

                int previousUses = item.totalUses;
                item.UIData.hideFuel = uses <= MinimumUses;
                item.totalUses = uses;

                if (item != registeredItem && item.HasData(DataEntryKey.ItemUses))
                {
                    OptionableIntItemData remainingUses = item.GetData<OptionableIntItemData>(
                        DataEntryKey.ItemUses
                    );
                    if (remainingUses.HasData && remainingUses.Value == previousUses)
                    {
                        remainingUses.Value = uses;
                        item.SetUseRemainingPercentage(1f);
                    }
                }
            }
        }

        if (changed)
        {
            Log.LogInfo($"Arrow Blowgun uses set to {uses} from {source}.");
        }
    }

    internal void ApplyEffectiveAddToLootPool(bool addToLootPool, string source)
    {
        bool changed = effectiveAddToLootPool != addToLootPool;
        effectiveAddToLootPool = addToLootPool;

        Item? registeredItem = RegisteredItem;
        if (registeredItem != null)
        {
            foreach (Item item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item != null && item.itemID == registeredItem.itemID)
                {
                    ApplyLootPoolSetting(item);
                }
            }

            LootData.AllSpawnWeightData = null;
        }

        if (changed)
        {
            Log.LogInfo(
                $"Arrow Blowgun random loot spawning "
                    + $"{(addToLootPool ? "enabled" : "disabled")} from {source}."
            );
        }
    }

    private void ApplyLootPoolSetting(Item item)
    {
        LootData? lootData = item.GetComponent<LootData>();
        if (lootData != null)
        {
            lootData.spawnLocations = effectiveAddToLootPool
                ? originalLootSpawnLocations
                : SpawnPool.None;
        }
    }

    private void ValidateFloatConfig(
        ConfigEntry<float> config,
        float defaultValue,
        float minimum,
        float maximum
    )
    {
        float value = config.Value;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            Log.LogWarning(
                $"Configured {config.Definition.Key} value {value} is invalid; "
                    + $"using {defaultValue}."
            );
            config.Value = defaultValue;
            return;
        }

        float clampedValue = Mathf.Clamp(value, minimum, maximum);
        if (!Mathf.Approximately(value, clampedValue))
        {
            Log.LogWarning(
                $"Configured {config.Definition.Key} value {value} is outside the "
                    + $"supported range {minimum}-{maximum}; using {clampedValue}."
            );
            config.Value = clampedValue;
        }
    }

    private static void RegisterItem(ItemDatabase database, Item item)
    {
        item.gameObject.name = $"{PluginGuid}:{PrefabName}";
        item.itemID = GetStableItemId(database, item.gameObject.name);

        string prefabId = ItemPrefabFolder + item.gameObject.name;
        PhotonNetwork.PrefabPool = new ArrowBlowgunPrefabPool(
            PhotonNetwork.PrefabPool,
            prefabId,
            item.gameObject
        );

        database.Objects.Add(item);
        database.itemLookup.Add(item.itemID, item);
    }

    private static ushort GetStableItemId(ItemDatabase database, string uniqueName)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(uniqueName));
        ushort initialId = BitConverter.ToUInt16(hash, 0);
        ushort candidate = initialId;

        do
        {
            if (!database.itemLookup.ContainsKey(candidate))
            {
                return candidate;
            }

            candidate++;
        } while (candidate != initialId);

        throw new InvalidOperationException("No free item ID is available for ArrowBlowgun.");
    }

    private static void RegisterItemName()
    {
        string lookupKey = LocalizedText.GetNameIndex(ItemNameKey).ToUpperInvariant();
        int languageCount = LocalizedText.mainTable.Values.FirstOrDefault()?.Count
            ?? Enum.GetValues(typeof(LocalizedText.Language)).Length;

        List<string> names = Enumerable.Repeat("Arrow Blowgun", languageCount).ToList();
        SetLocalizedName(names, LocalizedText.Language.SimplifiedChinese, "箭矢吹箭筒");
        SetLocalizedName(names, LocalizedText.Language.TraditionalChinese, "箭矢吹箭筒");

        LocalizedText.mainTable[lookupKey] = names;
    }

    private static void SetLocalizedName(
        List<string> names,
        LocalizedText.Language language,
        string value
    )
    {
        int index = (int)language;
        if (index < names.Count)
        {
            names[index] = value;
        }
    }
}
