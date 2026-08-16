using Peak;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArrowBlowgun;

internal static class ArrowVisualFactory
{
    private enum ArrowAxis
    {
        Forward,
        NegativeUp,
    }

    private static GameObject? cacheRoot;
    private static GameObject? template;
    private static ArrowAxis templateAxis;
    private static Material? fallbackMaterial;

    internal static void WarmUp()
    {
        EnsureTemplate();
    }

    internal static GameObject Create(Vector3 position, Vector3 direction)
    {
        EnsureTemplate();

        GameObject arrow = Object.Instantiate(template!);
        arrow.name = "ArrowBlowgun.ArrowVisual";
        arrow.transform.SetParent(null, worldPositionStays: false);
        SceneManager.MoveGameObjectToScene(arrow, SceneManager.GetActiveScene());
        arrow.transform.position = position;
        SetDirection(arrow, direction);
        arrow.SetActive(value: true);
        return arrow;
    }

    internal static void SetDirection(GameObject arrow, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 localAxis = templateAxis == ArrowAxis.Forward ? Vector3.forward : Vector3.down;
        arrow.transform.rotation = Quaternion.FromToRotation(localAxis, direction.normalized);
    }

    internal static void Embed(
        GameObject arrow,
        Vector3 position,
        Vector3 direction,
        Vector3 surfaceNormal
    )
    {
        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : -direction.normalized;
        arrow.name = "ArrowBlowgun.StuckArrow";
        arrow.transform.position = position + normal * 0.01f;
        SetDirection(arrow, direction);
    }

    private static void EnsureTemplate()
    {
        if (template != null)
        {
            return;
        }

        EnsureCacheRoot();

        GameObject? source = FindVanillaArrowSource(out ArrowAxis axis, out string sourceKind);
        if (source != null)
        {
            Vector3 sourceLocalScale = source.transform.localScale;
            Vector3 sourceWorldScale = Abs(source.transform.lossyScale);
            template = (GameObject)Object.Instantiate(
                source,
                cacheRoot!.transform,
                instantiateInWorldSpace: true
            );
            template.name = "VanillaArrowVisual.Template";
            template.transform.localScale = sourceWorldScale;
            template.SetActive(value: false);
            templateAxis = axis;
            Sanitize(template);
            Plugin.Log.LogInfo(
                $"Using vanilla arrow visual from {sourceKind} '{source.name}' "
                    + $"with local scale {sourceLocalScale} and world scale {sourceWorldScale}."
            );
            return;
        }

        template = CreateFallbackTemplate();
        templateAxis = ArrowAxis.Forward;
        Plugin.Log.LogWarning(
            "No loaded vanilla arrow visual was found; using the built-in fallback visual."
        );
    }

    private static void EnsureCacheRoot()
    {
        if (cacheRoot != null)
        {
            return;
        }

        cacheRoot = new GameObject($"{Plugin.PluginGuid}.ArrowVisualCache");
        cacheRoot.SetActive(value: false);
        Object.DontDestroyOnLoad(cacheRoot);
    }

    private static GameObject? FindVanillaArrowSource(
        out ArrowAxis axis,
        out string sourceKind
    )
    {
        foreach (ArrowShooter shooter in Resources.FindObjectsOfTypeAll<ArrowShooter>())
        {
            if (shooter != null && shooter.arrowPrefab != null)
            {
                axis = ArrowAxis.Forward;
                sourceKind = nameof(ArrowShooter);
                return shooter.arrowPrefab;
            }
        }

        foreach (
            GenerateCharacterArrows generator in Resources.FindObjectsOfTypeAll<GenerateCharacterArrows>()
        )
        {
            if (generator != null && generator.arrowPrefab != null)
            {
                axis = ArrowAxis.NegativeUp;
                sourceKind = nameof(GenerateCharacterArrows);
                return generator.arrowPrefab;
            }
        }

        GameObject? physicalArrow = FindPhysicalCharacterArrow();
        if (physicalArrow != null)
        {
            axis = ArrowAxis.NegativeUp;
            sourceKind = nameof(ThornOnMe);
            return physicalArrow;
        }

        axis = ArrowAxis.Forward;
        sourceKind = "fallback";
        return null;
    }

    private static GameObject? FindPhysicalCharacterArrow()
    {
        GameObject? localArrow = FindPhysicalCharacterArrow(Character.localCharacter);
        if (localArrow != null)
        {
            return localArrow;
        }

        foreach (Character candidate in Character.AllCharacters)
        {
            GameObject? arrow = FindPhysicalCharacterArrow(candidate);
            if (arrow != null)
            {
                return arrow;
            }
        }

        return null;
    }

    private static GameObject? FindPhysicalCharacterArrow(Character? character)
    {
        if (character == null || character.refs?.afflictions?.physicalThorns == null)
        {
            return null;
        }

        foreach (ThornOnMe thorn in character.refs.afflictions.physicalThorns)
        {
            if (thorn != null && thorn.isArrow)
            {
                return thorn.gameObject;
            }
        }

        return null;
    }

    private static void Sanitize(GameObject root)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
            Object.Destroy(behaviour);
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            Object.Destroy(collider);
        }

        foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rigidbody.detectCollisions = false;
            rigidbody.isKinematic = true;
            Object.Destroy(rigidbody);
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    private static GameObject CreateFallbackTemplate()
    {
        GameObject root = new("FallbackArrowVisual.Template");
        root.transform.SetParent(cacheRoot!.transform, worldPositionStays: false);
        root.SetActive(value: false);

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Shaft";
        shaft.transform.SetParent(root.transform, worldPositionStays: false);
        shaft.transform.localPosition = new Vector3(0f, 0f, -0.18f);
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.012f, 0.28f, 0.012f);

        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = "Tip";
        tip.transform.SetParent(root.transform, worldPositionStays: false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.11f);
        tip.transform.localScale = new Vector3(0.035f, 0.035f, 0.07f);

        Material? material = GetFallbackMaterial();
        if (material != null)
        {
            shaft.GetComponent<Renderer>().sharedMaterial = material;
            tip.GetComponent<Renderer>().sharedMaterial = material;
        }

        Sanitize(root);
        return root;
    }

    private static Material? GetFallbackMaterial()
    {
        if (fallbackMaterial != null)
        {
            return fallbackMaterial;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        fallbackMaterial = new Material(shader)
        {
            color = new Color(0.32f, 0.22f, 0.12f, 1f),
            hideFlags = HideFlags.HideAndDontSave,
        };
        return fallbackMaterial;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
