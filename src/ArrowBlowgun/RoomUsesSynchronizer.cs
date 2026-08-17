using ExitGames.Client.Photon;
using Photon.Pun;

namespace ArrowBlowgun;

internal sealed class RoomUsesSynchronizer : MonoBehaviourPunCallbacks
{
    private const string RoomPropertyKey = Plugin.PluginGuid + ".Uses";

    private Plugin plugin = null!;

    internal void Initialize(Plugin owner)
    {
        plugin = owner;

        if (PhotonNetwork.InRoom)
        {
            SynchronizeRoomUses();
        }
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        SynchronizeRoomUses();
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        plugin.ApplyEffectiveUses(plugin.ConfiguredUses, "local config");
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (propertiesThatChanged.ContainsKey(RoomPropertyKey))
        {
            TryApplyRoomUses();
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        base.OnMasterClientSwitched(newMasterClient);

        if (!TryApplyRoomUses() && PhotonNetwork.IsMasterClient)
        {
            PublishLocalUses();
        }
    }

    private void SynchronizeRoomUses()
    {
        if (!TryApplyRoomUses() && PhotonNetwork.IsMasterClient)
        {
            PublishLocalUses();
        }
    }

    private bool TryApplyRoomUses()
    {
        if (
            PhotonNetwork.CurrentRoom == null
            || !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                RoomPropertyKey,
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

    private void PublishLocalUses()
    {
        int uses = plugin.ConfiguredUses;
        plugin.ApplyEffectiveUses(uses, "host config");
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { [RoomPropertyKey] = uses }
        );
    }
}
