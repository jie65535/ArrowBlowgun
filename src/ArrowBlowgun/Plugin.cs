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
    internal const string PluginVersion = "0.1.0";

    private const string PrefabName = "ArrowBlowgun";
    private const string ItemNameKey = "ARROW_BLOWGUN";
    private const string ItemPrefabFolder = "0_Items/";
    private const int DefaultUses = 3;
    private const int MinimumUses = 1;

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Item? RegisteredItem { get; private set; }
    internal int ConfiguredUses => usesConfig.Value;

    private ConfigEntry<int> usesConfig = null!;
    private int effectiveUses = DefaultUses;
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

        if (usesConfig.Value < MinimumUses)
        {
            Log.LogWarning(
                $"Configured Uses value {usesConfig.Value} is invalid; using {MinimumUses}."
            );
            usesConfig.Value = MinimumUses;
        }

        effectiveUses = usesConfig.Value;
        gameObject.AddComponent<RoomUsesSynchronizer>().Initialize(this);

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
        arrowAction.CopyFrom(vanillaAction);

        item.UIData.itemName = ItemNameKey;
        item.UIData.isShootable = true;
        item.UIData.hideFuel = effectiveUses <= MinimumUses;
        item.totalUses = effectiveUses;
        RegisterItemName();

        RegisterItem(database, item);

        // A cloned LootData component will be discovered the next time loot weights are built.
        LootData.AllSpawnWeightData = null;
        RegisteredItem = item;

        Log.LogInfo(
            $"Registered {item.gameObject.name} from vanilla source {sourceItem.gameObject.name} "
                + $"with item ID {item.itemID} and {item.totalUses} uses."
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
