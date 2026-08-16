using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace ArrowBlowgun;

internal static class ArrowTrapFeedback
{
    private const string TrapSmokeMaterialName = "M_VFX_Smoke Light";

    private static readonly SoundResource[] SoundResources =
    {
        new("SFXI Arrow Shot 1", "Au_Hookshot_Shoot", "Au_Hookshot_Shoot.ogg"),
        new("SFXI Arrow Shot 2", "Au_Bow_Hit2", "Au_Bow_Hit2.ogg"),
        new("SFXI Arrow Shot 3", "Au_Bow_Release", "Au_Bow_Release.ogg"),
        new("SFXI Arrow Shot 4", "Au_Door5", "Au_Door5.ogg"),
    };

    private static readonly List<SFX_Instance> embeddedShotSounds = new();

    private static SFX_Instance? fallbackShotSound;
    private static Material? fallbackSmokeMaterial;
    private static Material? trapSmokeMaterial;
    private static bool initialized;
    private static bool warnedAboutMissingSound;

    internal static void ConfigureFallback(
        SFX_Instance? shotSound,
        IEnumerable<ParticleSystem> particles
    )
    {
        fallbackShotSound = shotSound;
        fallbackSmokeMaterial = particles
            .Where(particle => particle != null)
            .Select(particle => particle.GetComponent<ParticleSystemRenderer>()?.sharedMaterial)
            .FirstOrDefault(material => material != null);
    }

    internal static IEnumerator Initialize()
    {
        if (initialized)
        {
            yield break;
        }

        initialized = true;
        Assembly assembly = typeof(ArrowTrapFeedback).Assembly;

        foreach (SoundResource soundResource in SoundResources)
        {
            string resourceName = $"ArrowBlowgun.Assets.Audio.{soundResource.FileName}";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Plugin.Log.LogError($"Embedded arrow-trap sound is missing: {resourceName}");
                continue;
            }

            byte[] audioData;
            using (MemoryStream memory = new())
            {
                stream.CopyTo(memory);
                audioData = memory.ToArray();
            }

            string temporaryPath = Path.Combine(
                Path.GetTempPath(),
                $"{Plugin.PluginGuid}.{Guid.NewGuid():N}.ogg"
            );

            try
            {
                File.WriteAllBytes(temporaryPath, audioData);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Could not stage arrow-trap sound '{soundResource.ClipName}': {exception.Message}"
                );
                continue;
            }

            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                new Uri(temporaryPath),
                AudioType.OGGVORBIS
            );
            DownloadHandlerAudioClip downloadHandler = (DownloadHandlerAudioClip)request.downloadHandler;
            downloadHandler.streamAudio = false;
            downloadHandler.compressed = false;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                clip.name = soundResource.ClipName;
                embeddedShotSounds.Add(CreateSoundInstance(soundResource.InstanceName, clip));
            }
            else
            {
                Plugin.Log.LogError(
                    $"Could not load arrow-trap sound '{soundResource.ClipName}': {request.error}"
                );
            }

            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogDebug(
                    $"Could not delete temporary arrow-trap audio '{temporaryPath}': {exception.Message}"
                );
            }
        }

        Plugin.Log.LogInfo(
            $"Loaded {embeddedShotSounds.Count}/{SoundResources.Length} arrow-trap sound layers."
        );
    }

    internal static void Play(Vector3 origin, Vector3 endpoint, Vector3 direction)
    {
        ArrowShooter? loadedShooter = FindLoadedArrowShooter();
        PlaySounds(origin, loadedShooter);

        if (loadedShooter != null)
        {
            PlayLoadedTrapParticles(loadedShooter, origin, endpoint, direction);
            return;
        }

        PlayRecreatedMuzzlePuff(origin, direction);
        PlayRecreatedSmokeTrail(origin, endpoint);
    }

    private static SFX_Instance CreateSoundInstance(string instanceName, AudioClip clip)
    {
        SFX_Instance sound = ScriptableObject.CreateInstance<SFX_Instance>();
        sound.name = instanceName;
        sound.hideFlags = HideFlags.HideAndDontSave;
        sound.clips = new[] { clip };
        sound.settings = new SFX_Settings
        {
            volume = 0.5f,
            volume_Variation = 0.1f,
            pitch = 1f,
            pitch_Variation = 0.2f,
            spatialBlend = 1f,
            dopplerLevel = 1f,
            range = 100f,
            cooldown = 0.02f,
            maxInstances_NOT_IMPLEMENTED = 5,
        };
        return sound;
    }

    private static void PlaySounds(Vector3 origin, ArrowShooter? loadedShooter)
    {
        IEnumerable<SFX_Instance> sounds = loadedShooter?.shotSFX is { } loadedSounds
            ? loadedSounds
            : embeddedShotSounds;
        bool playedSound = false;
        foreach (SFX_Instance sound in sounds)
        {
            if (sound != null)
            {
                sound.Play(origin);
                playedSound = true;
            }
        }

        if (playedSound)
        {
            return;
        }

        if (fallbackShotSound != null)
        {
            fallbackShotSound.Play(origin);
            return;
        }

        if (!warnedAboutMissingSound)
        {
            warnedAboutMissingSound = true;
            Plugin.Log.LogWarning("No arrow-trap shot sound is available.");
        }
    }

    private static ArrowShooter? FindLoadedArrowShooter()
    {
        return Resources
            .FindObjectsOfTypeAll<ArrowShooter>()
            .FirstOrDefault(shooter =>
                shooter != null
                && shooter.firedParticles != null
                && shooter.trailParticles != null
            );
    }

    private static void PlayLoadedTrapParticles(
        ArrowShooter shooter,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 direction
    )
    {
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        ParticleSystem muzzle = UnityEngine.Object.Instantiate(
            shooter.firedParticles,
            origin,
            rotation
        );
        muzzle.gameObject.SetActive(value: true);
        muzzle.Play(withChildren: true);
        UnityEngine.Object.Destroy(muzzle.gameObject, 6f);

        Vector3 path = endpoint - origin;
        ParticleSystem trail = UnityEngine.Object.Instantiate(
            shooter.trailParticles,
            origin + path * 0.5f,
            Quaternion.LookRotation(path, Vector3.up)
        );
        ParticleSystem.ShapeModule shape = trail.shape;
        shape.radius = path.magnitude * 0.5f;
        trail.gameObject.SetActive(value: true);
        trail.Play(withChildren: true);
        UnityEngine.Object.Destroy(trail.gameObject, 6f);
    }

    private static void PlayRecreatedMuzzlePuff(Vector3 origin, Vector3 direction)
    {
        ParticleSystem particles = CreateParticleSystem(
            "ArrowBlowgun.ArrowTrapMuzzle",
            origin,
            Quaternion.LookRotation(direction, Vector3.up)
        );
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.04f;

        ConfigureFade(particles);
        particles.Play();
    }

    private static void PlayRecreatedSmokeTrail(Vector3 origin, Vector3 endpoint)
    {
        Vector3 path = endpoint - origin;
        if (path.sqrMagnitude < 0.0001f)
        {
            return;
        }

        ParticleSystem particles = CreateParticleSystem(
            "ArrowBlowgun.ArrowTrapSmokeTrail",
            origin + path * 0.5f,
            Quaternion.LookRotation(path, Vector3.up)
        );
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 1000;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
        shape.radius = path.magnitude * 0.5f;
        shape.rotation = new Vector3(0f, 90f, 0f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.05f;

        ConfigureFade(particles);
        particles.Play();
    }

    private static ParticleSystem CreateParticleSystem(
        string name,
        Vector3 position,
        Quaternion rotation
    )
    {
        GameObject gameObject = new(name);
        gameObject.transform.SetPositionAndRotation(position, rotation);

        ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetSmokeMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = 0.5f;

        return particles;
    }

    private static void ConfigureFade(ParticleSystem particles)
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.8f, 0f),
                new GradientAlphaKey(0f, 1f),
            }
        );

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static Material? GetSmokeMaterial()
    {
        if (trapSmokeMaterial == null)
        {
            trapSmokeMaterial = Resources
                .FindObjectsOfTypeAll<Material>()
                .FirstOrDefault(material => material != null && material.name == TrapSmokeMaterialName);
        }

        return trapSmokeMaterial != null ? trapSmokeMaterial : fallbackSmokeMaterial;
    }

    private sealed class SoundResource
    {
        internal SoundResource(string instanceName, string clipName, string fileName)
        {
            InstanceName = instanceName;
            ClipName = clipName;
            FileName = fileName;
        }

        internal string InstanceName { get; }
        internal string ClipName { get; }
        internal string FileName { get; }
    }
}
