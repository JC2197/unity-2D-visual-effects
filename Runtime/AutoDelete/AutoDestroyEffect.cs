using UnityEngine;
using System.Collections;

/// <summary>
/// Automatically destroys a GameObject after its animation or particle effect completes.
/// Attach to VFX prefabs like lightning strikes, explosions, impact effects, etc.
/// Supports both Animator-based effects and ParticleSystem-based effects.
/// </summary>

namespace JoeConticello.VisualEffects
{
    public class AutoDestroyEffect : MonoBehaviour
    {
        private const float DefaultMinimumLifetime = 0.5f;
        private const float DefaultMaximumLifetime = 5f;

        [Header("Destruction Settings")]
        [SerializeField] private bool destroyOnAnimationComplete = true;
        [SerializeField] private bool destroyOnParticleComplete = true;
        [SerializeField] private float additionalDelay = 0f;
        [SerializeField] private bool useManualLifetime = false;
        [SerializeField] private float manualLifetime = 2f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        private Animator animator;
        private ParticleSystem[] particleSystems;
        private float spawnTime;
        private bool isDestroying = false;

        private void Awake()
        {
            spawnTime = Time.time;

            // Find components
            animator = GetComponent<Animator>();
            particleSystems = GetComponentsInChildren<ParticleSystem>();

            if (showDebugLogs)
            {
                Debug.Log($"[AutoDestroyEffect] {gameObject.name} initialized. Animator: {animator != null}, ParticleSystems: {particleSystems.Length}");
            }
        }

        private void Start()
        {
            // Use manual lifetime if specified
            if (useManualLifetime)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"[AutoDestroyEffect] {gameObject.name} using manual lifetime: {manualLifetime}s");
                }
                Destroy(gameObject, manualLifetime);
                return;
            }

            // Start checking for completion
            StartCoroutine(CheckForCompletion());
        }

        private IEnumerator CheckForCompletion()
        {
            // Wait one frame to ensure everything is initialized
            yield return null;

            bool hasAnimator = animator != null && destroyOnAnimationComplete;
            bool hasParticles = particleSystems != null && particleSystems.Length > 0 && destroyOnParticleComplete;

            if (!hasAnimator && !hasParticles)
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"[AutoDestroyEffect] {gameObject.name} has no Animator or ParticleSystems to track. Using manual lifetime fallback.");
                }
                Destroy(gameObject, manualLifetime);
                yield break;
            }

            // Wait for animation to complete
            if (hasAnimator)
            {
                yield return StartCoroutine(WaitForAnimationComplete());
            }

            // Wait for particles to complete
            if (hasParticles)
            {
                yield return StartCoroutine(WaitForParticlesComplete());
            }

            // Wait for additional delay
            if (additionalDelay > 0f)
            {
                if (showDebugLogs)
                {
                    Debug.Log($"[AutoDestroyEffect] {gameObject.name} waiting additional {additionalDelay}s before destruction");
                }
                yield return new WaitForSeconds(additionalDelay);
            }

            // Destroy the GameObject
            DestroyEffect();
        }

        private IEnumerator WaitForAnimationComplete()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                yield break;
            }

            // Wait for animator to start playing
            yield return null;

            // Get the current animation state
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            float animationLength = stateInfo.length;

            if (showDebugLogs)
            {
                Debug.Log($"[AutoDestroyEffect] {gameObject.name} animation length: {animationLength}s");
            }

            // Wait for animation to complete (normalized time >= 1)
            while (animator != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            {
                yield return null;
            }

            if (showDebugLogs)
            {
                Debug.Log($"[AutoDestroyEffect] {gameObject.name} animation completed");
            }
        }

        private IEnumerator WaitForParticlesComplete()
        {
            if (particleSystems == null || particleSystems.Length == 0)
            {
                yield break;
            }

            if (showDebugLogs)
            {
                Debug.Log($"[AutoDestroyEffect] {gameObject.name} waiting for {particleSystems.Length} particle systems to complete");
            }

            // Wait until all particle systems are done
            bool allParticlesComplete = false;
            while (!allParticlesComplete)
            {
                allParticlesComplete = true;

                foreach (ParticleSystem ps in particleSystems)
                {
                    if (ps != null && ps.IsAlive(true))
                    {
                        allParticlesComplete = false;
                        break;
                    }
                }

                if (!allParticlesComplete)
                {
                    yield return new WaitForSeconds(0.1f); // Check every 0.1 seconds
                }
            }

            if (showDebugLogs)
            {
                Debug.Log($"[AutoDestroyEffect] {gameObject.name} all particles completed");
            }
        }

        private void DestroyEffect()
        {
            if (isDestroying) return;
            isDestroying = true;

            if (showDebugLogs)
            {
                float lifetime = Time.time - spawnTime;
                Debug.Log($"[AutoDestroyEffect] Destroying {gameObject.name} after {lifetime:F2}s");
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Manually trigger destruction (useful for external scripts)
        /// </summary>
        public void DestroyNow()
        {
            DestroyEffect();
        }

        /// <summary>
        /// Manually trigger destruction after a delay
        /// </summary>
        public void DestroyAfterDelay(float delay)
        {
            if (isDestroying) return;
            StartCoroutine(DestroyAfterDelayCoroutine(delay));
        }

        private IEnumerator DestroyAfterDelayCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            DestroyEffect();
        }

        public static void SetupAutoDestroy(GameObject effect, float fallbackLifetime = 2f)
        {
            if (effect == null || effect.GetComponent<AutoDestroyEffect>() != null)
                return;

            float lifetime = CalculateLifetime(effect, fallbackLifetime);
            Destroy(effect, lifetime);
        }

        public static float CalculateLifetime(GameObject effect, float fallbackLifetime = 2f)
        {
            if (effect == null)
                return fallbackLifetime;

            float maxDuration = 0f;

            ParticleSystem[] particles = effect.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particleSystem in particles)
            {
                var main = particleSystem.main;
                float duration = main.duration + main.startLifetime.constantMax;
                if (duration > maxDuration)
                    maxDuration = duration;
            }

            Animator[] animators = effect.GetComponentsInChildren<Animator>(true);
            foreach (Animator currentAnimator in animators)
            {
                if (currentAnimator.runtimeAnimatorController == null)
                    continue;

                foreach (AnimationClip clip in currentAnimator.runtimeAnimatorController.animationClips)
                {
                    if (clip.length > maxDuration)
                        maxDuration = clip.length;
                }
            }

            if (maxDuration <= 0f)
                maxDuration = fallbackLifetime;

            return Mathf.Clamp(maxDuration, DefaultMinimumLifetime, DefaultMaximumLifetime);
        }
    }

}
