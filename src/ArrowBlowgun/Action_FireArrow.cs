using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace ArrowBlowgun;

internal sealed class Action_FireArrow : ItemAction
{
    private const float ArrowShooterRange = 80f;
    private const float MaxAimCorrectionDegrees = 35f;
    private const float Knockback = 30f;
    private const float KnockbackRadius = 400f;

    [SerializeField]
    private float maxDistance = ArrowShooterRange;

    [SerializeField]
    private Transform spawnTransform = null!;

    internal Transform SpawnTransform => spawnTransform;

    internal void CopyFrom(Action_RaycastDart source)
    {
        maxDistance = source.maxDistance > 0f ? source.maxDistance : ArrowShooterRange;
        spawnTransform = source.spawnTransform;
        ArrowTrapFeedback.ConfigureFallback(
            source.shotSFX,
            source.GetComponentsInChildren<ParticleSystem>(includeInactive: true)
        );

        OnPressed = source.OnPressed;
        OnHeld = source.OnHeld;
        OnReleased = source.OnReleased;
        OnCastFinished = source.OnCastFinished;
        OnCancelled = source.OnCancelled;
        OnSecondaryCastFinished = source.OnSecondaryCastFinished;
        OnSecondaryPressed = source.OnSecondaryPressed;
        OnSecondaryHeld = source.OnSecondaryHeld;
        OnSecondaryCancelled = source.OnSecondaryCancelled;
        OnConsumed = source.OnConsumed;
    }

    public override void RunAction()
    {
        if (character == null || !character.IsLocal || MainCamera.instance == null)
        {
            return;
        }

        Vector3 origin = spawnTransform != null ? spawnTransform.position : transform.position;
        Transform cameraTransform = MainCamera.instance.transform;
        Vector3 cameraDirection = cameraTransform.forward.normalized;

        RaycastHit aimHit = FindFirstValidHit(
            cameraTransform.position,
            cameraDirection,
            maxDistance
        );
        Vector3 aimPoint = aimHit.collider != null
            ? aimHit.point
            : cameraTransform.position + cameraDirection * maxDistance;
        Vector3 barrelDirection = spawnTransform != null
            ? spawnTransform.forward.normalized
            : transform.forward.normalized;
        if (barrelDirection.sqrMagnitude < Mathf.Epsilon)
        {
            barrelDirection = cameraDirection;
        }

        Vector3 toAimPoint = aimPoint - origin;
        Vector3 direction = barrelDirection;

        // Never steer toward a camera hit that lies behind the muzzle plane.
        if (Vector3.Dot(toAimPoint, barrelDirection) > 0f)
        {
            Vector3 desiredDirection = toAimPoint.sqrMagnitude > Mathf.Epsilon
                ? toAimPoint.normalized
                : cameraDirection;
            direction = Vector3
                .RotateTowards(
                    barrelDirection,
                    desiredDirection,
                    MaxAimCorrectionDegrees * Mathf.Deg2Rad,
                    0f
                )
                .normalized;
        }

        RaycastHit hit = FindFirstValidHit(origin, direction, maxDistance);

        bool hasImpact = hit.collider != null;
        Vector3 endpoint = hasImpact ? hit.point : origin + direction * maxDistance;
        Character? hitCharacter = hit.collider?.GetComponentInParent<Character>();
        Vector3 surfaceNormal = hitCharacter != null
            ? -direction
            : hasImpact
                ? hit.normal
                : -direction;

        int targetViewId = -1;
        if (hitCharacter != null)
        {
            targetViewId = hitCharacter.photonView.ViewID;
        }

        ApplyArrowShot(
            targetViewId,
            origin,
            endpoint,
            direction,
            surfaceNormal,
            hasImpact,
            Knockback
        );

        if (PhotonNetwork.InRoom && photonView.ViewID != 0)
        {
            photonView.RPC(
                nameof(RPCA_ArrowShot),
                RpcTarget.Others,
                targetViewId,
                origin,
                endpoint,
                direction,
                surfaceNormal,
                hasImpact,
                Knockback
            );
        }
    }

    private RaycastHit FindFirstValidHit(Vector3 origin, Vector3 direction, float distance)
    {
        return Physics
            .RaycastAll(
                origin,
                direction,
                distance,
                HelperFunctions.AllPhysical,
                QueryTriggerInteraction.Ignore
            )
            .OrderBy(hit => hit.distance)
            .FirstOrDefault(hit =>
            {
                if (hit.collider == null || hit.collider.GetComponentInParent<Item>() != null)
                {
                    return false;
                }

                Character? candidate = hit.collider.GetComponentInParent<Character>();
                return candidate == null || candidate != character;
            });
    }

    [PunRPC]
    private void RPCA_ArrowShot(
        int targetViewId,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 direction,
        Vector3 surfaceNormal,
        bool hasImpact,
        float knockback
    )
    {
        ApplyArrowShot(
            targetViewId,
            origin,
            endpoint,
            direction,
            surfaceNormal,
            hasImpact,
            knockback
        );
    }

    private void ApplyArrowShot(
        int targetViewId,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 direction,
        Vector3 surfaceNormal,
        bool hasImpact,
        float knockback
    )
    {
        ArrowTrapFeedback.Play(origin, endpoint, direction);

        if (!hasImpact)
        {
            return;
        }

        GamefeelHandler.instance?.AddPerlinShakeProximity(endpoint, 5f);

        if (targetViewId < 0)
        {
            GameObject arrow = ArrowVisualFactory.Create(endpoint, direction);
            ArrowVisualFactory.Embed(arrow, endpoint, direction, surfaceNormal);
            return;
        }

        PhotonView targetView = PhotonNetwork.GetPhotonView(targetViewId);
        Character? target = targetView != null ? targetView.GetComponent<Character>() : null;
        if (target == null || !target.photonView.IsMine)
        {
            return;
        }

        target.refs.afflictions.AddArrow(endpoint, -direction);
        target.AddForceAtPosition(direction * knockback, endpoint, KnockbackRadius);
    }
}
