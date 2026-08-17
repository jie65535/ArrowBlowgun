using ExitGames.Client.Photon;
using Photon.Pun;

namespace ArrowBlowgun;

internal sealed class RoomConfigSynchronizer : MonoBehaviourPunCallbacks
{
    private const string UsesPropertyKey = Plugin.PluginGuid + ".Uses";
    private const string AddToLootPoolPropertyKey = Plugin.PluginGuid + ".AddToLootPool";

    private Plugin plugin = null!;

    internal void Initialize(Plugin owner)
    {
        plugin = owner;

        if (PhotonNetwork.InRoom)
        {
            SynchronizeRoomConfig();
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        SynchronizeRoomConfig();
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        plugin.ApplyEffectiveUses(plugin.ConfiguredUses, "local config");
        plugin.ApplyEffectiveAddToLootPool(plugin.ConfiguredAddToLootPool, "local config");
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (propertiesThatChanged.ContainsKey(UsesPropertyKey))
        {
            TryApplyRoomUses();
        }

        if (propertiesThatChanged.ContainsKey(AddToLootPoolPropertyKey))
        {
            TryApplyRoomLootPoolSetting();
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);
        SynchronizeRoomConfig();
    }

    private void SynchronizeRoomConfig()
    {
        bool hasRoomUses = TryApplyRoomUses();
        bool hasRoomLootPoolSetting = TryApplyRoomLootPoolSetting();

        if (PhotonNetwork.IsMasterClient)
        {
            PublishMissingRoomConfig(hasRoomUses, hasRoomLootPoolSetting);
        }
    }

    private bool TryApplyRoomUses()
    {
        if (
            PhotonNetwork.CurrentRoom == null
            || !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                UsesPropertyKey,
                out object value
            )
            || value is not int uses
        )
        {
            return false;
        }

        plugin.ApplyEffectiveUses(uses, "room config");
        return true;
    }

    private bool TryApplyRoomLootPoolSetting()
    {
        if (
            PhotonNetwork.CurrentRoom == null
            || !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                AddToLootPoolPropertyKey,
                out object value
            )
            || value is not bool addToLootPool
        )
        {
            return false;
        }

        plugin.ApplyEffectiveAddToLootPool(addToLootPool, "room config");
        return true;
    }

    private void PublishMissingRoomConfig(bool hasRoomUses, bool hasRoomLootPoolSetting)
    {
        if (PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable properties = new();
        if (!hasRoomUses)
        {
            int uses = plugin.ConfiguredUses;
            plugin.ApplyEffectiveUses(uses, "host config");
            properties[UsesPropertyKey] = uses;
        }

        if (!hasRoomLootPoolSetting)
        {
            bool addToLootPool = plugin.ConfiguredAddToLootPool;
            plugin.ApplyEffectiveAddToLootPool(addToLootPool, "host config");
            properties[AddToLootPoolPropertyKey] = addToLootPool;
        }

        if (properties.Count > 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        }
    }
}
