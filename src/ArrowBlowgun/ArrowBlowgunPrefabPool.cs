using Photon.Pun;
using UnityEngine;

namespace ArrowBlowgun;

internal sealed class ArrowBlowgunPrefabPool : IPunPrefabPool
{
    private readonly IPunPrefabPool fallback;
    private readonly string prefabId;
    private readonly GameObject prefab;

    internal ArrowBlowgunPrefabPool(
        IPunPrefabPool fallback,
        string prefabId,
        GameObject prefab
    )
    {
        this.fallback = fallback;
        this.prefabId = prefabId;
        this.prefab = prefab;
    }

    public GameObject Instantiate(string requestedPrefabId, Vector3 position, Quaternion rotation)
    {
        if (requestedPrefabId != prefabId)
        {
            return fallback.Instantiate(requestedPrefabId, position, rotation);
        }

        bool activeSelf = prefab.activeSelf;
        if (activeSelf)
        {
            prefab.SetActive(false);
        }

        GameObject instance = Object.Instantiate(prefab, position, rotation);

        if (activeSelf)
        {
            prefab.SetActive(true);
        }

        return instance;
    }

    public void Destroy(GameObject gameObject)
    {
        fallback.Destroy(gameObject);
    }
}
