using System.Collections.Generic;
using UnityEngine;
using TMPro;
using RhythmWitchClone.Audio;
using RhythmWitchClone.Levels;
using RhythmWitchClone.Core;

namespace RhythmWitchClone.Gameplay
{
    /// <summary>
    /// Gegner-Verhalten: bewegt sich auf den Spieler zu. Ist der Spieler innerhalb von "Attack Range",
    /// bekommt er Schaden, der Gegner selbst wird vom Spieler weg zurückgestoßen (Knockback) und
    /// braucht danach eine kurze Cooldown-Zeit, bevor er erneut angreifen kann.
    ///
    /// Zusätzlich: Gesundheit inkl. Tod (TakeDamage wird von PlayerController.UpdateProjectiles
    /// bei einem Projektil-Treffer aufgerufen), leichter Knockback bei Treffern, und ein kurz
    /// erscheinender Schadenstext über dem Gegner. Keine visuelle HP-Leiste, wie gewünscht.
    ///
    /// Wiederverwendbar für mehrere Gegner-Typen: Dieses eine Script als Prefab anlegen, für
    /// jede Gegner-Variante das Prefab duplizieren und nur Werte (Tempo, Schaden, Reichweite, ...)
    /// sowie das Aussehen (Textur am Visual-Kind) anpassen - kein neues Script pro Gegnertyp nötig.
    ///
    /// Setup:
    /// - Kommt auf das Gegner-Root-GameObject (bekommt automatisch einen CharacterController).
    /// - "Visual Transform" zeigt auf das Kind-Objekt mit Quad + BillboardSprite-Script,
    ///   genau wie beim Spieler.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(100)] // nach BillboardSprite, damit die Rückschlag-Neigung nicht überschrieben wird
    public class EnemyController : MonoBehaviour
    {
        [Header("Bewegung")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Angriff")]
        [Tooltip("Abstand zum Spieler, ab dem der Gegner angreift statt weiter zu verfolgen.")]
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private int damage = 10;
        [SerializeField] private float attackCooldown = 1f;

        [Header("Knockback (nach einem erfolgreichen Angriff)")]
        [SerializeField] private float knockbackForce = 6f;
        [SerializeField] private float knockbackDuration = 0.25f;

        [Header("Gesundheit")]
        [SerializeField] private int maxHealth = 50;
        private int _currentHealth;

        [Header("Rückschlag (durch einen Spieler-Treffer)")]
        [Tooltip("Wie kraftvoll der Gegner bei einem Treffer weggeschleudert wird.")]
        [SerializeField] private float hitLaunchForce = 14f;
        [Tooltip("Wie lange der Rückschlag dauert - startet schnell und bremst stark ab.")]
        [SerializeField] private float hitLaunchDuration = 0.4f;
        [Tooltip("Wie hoch der Gegner beim Rückschlag kurz abhebt (nur optisch).")]
        [SerializeField] private float hitLaunchHopHeight = 0.5f;
        [Tooltip("Wie stark sich der Gegner beim Rückschlag nach hinten neigt (Grad).")]
        [SerializeField] private float hitLeanAngle = 30f;
        [Tooltip("Kurzes Einfrieren im Moment des Treffers, bevor er wegfliegt - macht den Schlag spürbarer.")]
        [SerializeField] private float hitStopDuration = 0.07f;
        [Tooltip("Wie viel stärker der Rückschlag bei viel Schaden wird (1 = bis zu doppelt so stark, wenn ein Treffer die komplette HP abzieht).")]
        [Range(0f, 2f)][SerializeField] private float hitDamageForceScaling = 1f;

        [Header("Schadenstext")]
        [SerializeField] private Color damageTextColor = Color.red;
        [SerializeField] private float damageTextFontSize = 4f;
        [SerializeField] private float damageTextHeight = 2f;
        [SerializeField] private float damageTextLifetime = 0.8f;

        [Header("Treffer-Reaktion")]
        [Tooltip("Farbe, in der der Gegner bei einem Treffer kurz aufblitzt.")]
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private float hitFlashDuration = 0.15f;
        [Tooltip("Wie stark der Gegner bei einem Treffer gestaucht wird (0.3 = 30% breiter / niedriger).")]
        [SerializeField] private float hitSquashAmount = 0.3f;
        [Tooltip("Wie lange das Zurückfedern nach dem Stauchen dauert.")]
        [SerializeField] private float hitSquashDuration = 0.3f;
        [Tooltip("Wie stark das Bild bei einem Treffer wackelt (Welt-Einheiten).")]
        [SerializeField] private float hitShakeStrength = 0.12f;
        [SerializeField] private float hitShakeDuration = 0.2f;
        [Tooltip("Wie lange der Gegner nach einem Treffer kurz 'taumelt' (bleibt stehen, greift nicht an).")]
        [SerializeField] private float hitStaggerDuration = 0.2f;

        [Header("Tod")]
        [Tooltip("Wie lange die Todes-Animation dauert (aufblitzen, hochploppen, plattdrücken, ausblenden).")]
        [SerializeField] private float deathAnimationDuration = 0.4f;

        [Header("Sichtbares Bild")]
        [Tooltip("Das Kind-Objekt mit dem sichtbaren Bild (Quad mit BillboardSprite-Script drauf), analog zum Spieler.")]
        [SerializeField] private Transform visualTransform;

        private CharacterController _controller;
        private PlayerController _player;

        private Vector3 _verticalVelocity;
        private Vector3 _knockbackStartVelocity;
        private Vector3 _knockbackVelocity;
        private float _knockbackTimer;
        private float _knockbackDurationTotal; // welche Dauer gerade gilt - Angriff- und Treffer-Knockback haben unterschiedliche Werte
        private float _stunTimer; // Betäubung durch Spieler-Spezialfähigkeiten (siehe ApplyStun)

        private float _attackCooldownTimer;
        private Vector3 _visualBaseScale;
        private Vector3 _visualBaseLocalPosition;
        private float _facingSign = 1f;

        // Treffer-Reaktion
        private Renderer _visualRenderer;
        private Color _visualBaseColor = Color.white;
        private bool _hasBaseColor;
        private float _hitFlashTimer;
        private float _hitSquashTimer;
        private float _hitShakeTimer;
        private float _staggerTimer;
        private bool _isHitLaunch;          // aktueller Knockback stammt von einem Spieler-Treffer
        private float _hitStopTimer;
        private float _launchScreenSign = 1f; // Richtung auf dem Bildschirm (+1 = nach rechts)
        private Vector3 _deathVelocity;

        // Tod
        private bool _isDying;
        private float _deathTimer;

        private class FloatingDamageText
        {
            public TextMeshPro Text;
            public Vector3 StartPosition;
            public Vector3 Drift;
            public float Timer;
        }

        private readonly List<FloatingDamageText> _damageTexts = new List<FloatingDamageText>();

        /// <summary>True, sobald der Gegner besiegt ist und nur noch seine Todes-Animation läuft.</summary>
        public bool IsDying => _isDying;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (visualTransform != null)
            {
                _visualBaseScale = visualTransform.localScale;
                _visualBaseLocalPosition = visualTransform.localPosition;
                _visualRenderer = visualTransform.GetComponent<Renderer>();
            }

            // Verhindert, dass sich CharacterControllers von Gegnern gegenseitig wegschieben
            // (Unity würde sie sonst standardmäßig physikalisch auseinanderdrücken).
            // Setzt voraus, dass das Gegner-Prefab einer eigenen Layer zugewiesen ist (siehe Setup).
            Physics.IgnoreLayerCollision(gameObject.layer, gameObject.layer, true);
        }

        private void Start()
        {
            _currentHealth = maxHealth;

            _player = FindFirstObjectByType<PlayerController>();
            if (_player == null)
            {
                Debug.LogWarning("EnemyController: Kein PlayerController in der Szene gefunden.");
            }
        }

        private void Update()
        {
            UpdateDamageTexts();

            if (_isDying) return; // Todes-Animation läuft in LateUpdate

            if (_player == null) return;


            if (_attackCooldownTimer > 0f)
            {
                _attackCooldownTimer -= Time.deltaTime;
            }

            if (_stunTimer > 0f)
            {
                // Betäubt: keine Bewegung, kein Angriff - nur Knockback (falls gerade aktiv) und Schwerkraft laufen weiter.
                _stunTimer -= Time.deltaTime;

                if (_knockbackTimer > 0f)
                {
                    HandleKnockback();
                }
            }
            else if (_knockbackTimer > 0f)
            {
                HandleKnockback();
            }
            else if (_staggerTimer > 0f)
            {
                // Taumelt nach dem Rückschlag kurz - bleibt stehen, greift nicht an.
                _staggerTimer -= Time.deltaTime;
                _controller.Move(Vector3.up * _verticalVelocity.y * Time.deltaTime);
            }
            else
            {
                float distanceToPlayer = Vector3.Distance(transform.position, _player.transform.position);

                if (distanceToPlayer <= attackRange)
                {
                    HandleAttack();
                }
                else
                {
                    HandleChase();
                }
            }

            ApplyGravity();
        }

        private void LateUpdate()
        {
            // In LateUpdate, damit Neigung/Skalierung nach BillboardSprite angewendet werden.
            if (_isDying) UpdateDeathAnimation();
            else UpdateHitReaction();
        }

        /// <summary>
        /// Treffer-Feedback auf dem Bild: kurzes farbiges Aufblitzen, Stauchen mit Zurückfedern
        /// (wie ein Gummiball) und kurzes Wackeln. Läuft in LateUpdate, damit es nach der
        /// Blickrichtungs-Spiegelung angewendet wird.
        /// </summary>
        private void UpdateHitReaction()
        {
            if (visualTransform == null) return;

            // Farbe
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_hitFlashTimer / Mathf.Max(0.01f, hitFlashDuration));
                SetVisualColor(Color.Lerp(_visualBaseColor, hitFlashColor, t));
                if (_hitFlashTimer <= 0f) SetVisualColor(_visualBaseColor);
            }

            // Stauchen und zurückfedern (gedämpfte Schwingung)
            float squash = 0f;
            if (_hitSquashTimer > 0f)
            {
                _hitSquashTimer -= Time.deltaTime;
                float progress = 1f - Mathf.Clamp01(_hitSquashTimer / Mathf.Max(0.01f, hitSquashDuration));
                squash = hitSquashAmount * Mathf.Cos(progress * Mathf.PI * 3f) * (1f - progress);
            }

            // Rückschlag: abheben, nach hinten neigen, in Flugrichtung strecken.
            float launchHop = 0f;
            float launchLean = 0f;
            float launchStretch = 0f;
            if (_isHitLaunch && _knockbackTimer > 0f)
            {
                float t = Mathf.Clamp01(_knockbackTimer / Mathf.Max(0.01f, _knockbackDurationTotal)); // 1 -> 0
                float progress = 1f - t;
                launchHop = hitLaunchHopHeight * Mathf.Sin(progress * Mathf.PI);
                launchLean = hitLeanAngle * t;
                launchStretch = 0.2f * t;
            }

            float scaleX = 1f + squash + launchStretch;
            float scaleY = 1f - squash - launchStretch * 0.5f;
            visualTransform.localScale = new Vector3(
                Mathf.Abs(_visualBaseScale.x) * _facingSign * scaleX,
                _visualBaseScale.y * scaleY,
                _visualBaseScale.z);

            // Füße bleiben am Boden: beim Stauchen das Bild um die verlorene halbe Höhe absenken.
            Vector3 localPosition = _visualBaseLocalPosition + Vector3.down * ((1f - scaleY) * _visualBaseScale.y * 0.5f)
                                    + Vector3.up * launchHop;

            if (launchLean > 0.01f)
            {
                // Oberkörper kippt in Flugrichtung (Kopf "schnappt" nach hinten weg).
                visualTransform.rotation *= Quaternion.Euler(0f, 0f, -_launchScreenSign * launchLean);
            }

            // Wackeln
            if (_hitShakeTimer > 0f)
            {
                _hitShakeTimer -= Time.deltaTime;
                float strength = hitShakeStrength * Mathf.Clamp01(_hitShakeTimer / Mathf.Max(0.01f, hitShakeDuration));
                localPosition += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * strength;
            }

            visualTransform.localPosition = localPosition;
        }

        private void TriggerHitReaction()
        {
            CacheBaseColor();
            _hitFlashTimer = hitFlashDuration;
            _hitSquashTimer = hitSquashDuration;
            _hitShakeTimer = hitShakeDuration;
            _staggerTimer = hitStaggerDuration;
            _hitStopTimer = hitStopDuration;
        }

        private void CacheBaseColor()
        {
            // Erst beim ersten Treffer auslesen - das Material wird von BillboardSprite in Awake erzeugt.
            if (_hasBaseColor || _visualRenderer == null) return;
            _visualBaseColor = _visualRenderer.material.color;
            _hasBaseColor = true;
        }

        private void SetVisualColor(Color color)
        {
            if (_visualRenderer != null) _visualRenderer.material.color = color;
        }

        /// <summary>
        /// Startet die Todes-Animation statt den Gegner sofort zu löschen: Hitbox und KI aus,
        /// dann kurz hochploppen, plattdrücken und ausblenden. Das Objekt wird erst gelöscht,
        /// wenn Animation und Schadenszahlen fertig sind.
        /// </summary>
        private void StartDeath(Vector3 knockDirection, float force)
        {
            _isDying = true;
            _deathTimer = 0f;
            CacheBaseColor();

            // Der Todestreffer schleudert den Gegner zusätzlich weg (ohne Hitbox, rein optisch).
            _deathVelocity = knockDirection.sqrMagnitude > 0.0001f ? knockDirection.normalized * force : Vector3.zero;
            _launchScreenSign = GetScreenSign(knockDirection);

            if (_controller != null) _controller.enabled = false;

            if (_player != null)
            {
                _player.SpawnCoin(transform.position);
            }
        }

        private void UpdateDeathAnimation()
        {
            _deathTimer += Time.deltaTime;
            float duration = Mathf.Max(0.05f, deathAnimationDuration);
            float t = Mathf.Clamp01(_deathTimer / duration);

            // Wegfliegen mit starkem Abbremsen
            float slowdown = (1f - t) * (1f - t);
            transform.position += _deathVelocity * slowdown * Time.deltaTime;

            if (visualTransform != null)
            {
                // Erst kurz größer werden und hochhüpfen, dann flach in den Boden drücken.
                float pop = Mathf.Sin(Mathf.Clamp01(t * 2f) * Mathf.PI);           // 0 -> 1 -> 0 in der ersten Hälfte
                float flatten = Mathf.Clamp01((t - 0.4f) / 0.6f);                 // ab 40% platt werden
                float scaleX = 1f + 0.2f * pop + 0.6f * flatten;
                float scaleY = (1f + 0.25f * pop) * (1f - 0.9f * flatten);

                visualTransform.localScale = new Vector3(
                    Mathf.Abs(_visualBaseScale.x) * _facingSign * scaleX,
                    _visualBaseScale.y * scaleY,
                    _visualBaseScale.z);

                Vector3 hop = Vector3.up * (0.4f * pop);
                Vector3 keepFeetOnGround = Vector3.down * ((1f - scaleY) * _visualBaseScale.y * 0.5f);
                visualTransform.localPosition = _visualBaseLocalPosition + hop + keepFeetOnGround;

                // Farbe: Treffer-Farbe -> ausblenden
                Color color = Color.Lerp(hitFlashColor, _visualBaseColor, Mathf.Clamp01(t * 3f));
                color.a = _visualBaseColor.a * (1f - Mathf.Clamp01((t - 0.5f) / 0.5f));
                SetVisualColor(color);

                // Kippt in Flugrichtung um
                visualTransform.rotation *= Quaternion.Euler(0f, 0f, -_launchScreenSign * Mathf.Lerp(hitLeanAngle, 80f, t));

                if (t >= 1f) visualTransform.gameObject.SetActive(false);
            }

            if (t >= 1f && _damageTexts.Count == 0)
            {
                Destroy(gameObject);
            }
        }

        private void HandleChase()
        {
            Vector3 direction = _player.transform.position - transform.position;
            direction.y = 0f;
            direction.Normalize();

            FaceDirection(direction);

            float groundSpeedMultiplier = LevelManager.Instance != null ? LevelManager.Instance.GetSpeedMultiplierAt(transform.position) : 1f; // z.B. Büsche bremsen
            Vector3 motion = direction * moveSpeed * groundSpeedMultiplier + Vector3.up * _verticalVelocity.y;
            _controller.Move(motion * Time.deltaTime);
        }

        private void HandleAttack()
        {
            if (_attackCooldownTimer > 0f) return; // noch in der Angriffspause - einfach stehen bleiben

            _player.TakeDamage(damage);
            _attackCooldownTimer = attackCooldown;

            // Knockback vom Spieler weg auslösen.
            Vector3 knockDirection = transform.position - _player.transform.position;
            knockDirection.y = 0f;

            TriggerKnockback(knockDirection, knockbackForce, knockbackDuration);
        }

        /// <summary>
        /// Betäubt den Gegner für die angegebene Dauer (keine Bewegung, kein Angriff) und stößt
        /// ihn zusätzlich kurz in die übergebene Richtung weg - für Spieler-Spezialfähigkeiten
        /// wie eine Schockwelle.
        /// </summary>
        public void ApplyStun(Vector3 knockbackDirection, float knockbackForce, float stunDuration)
        {
            if (_isDying) return;
            _stunTimer = stunDuration;
            TriggerKnockback(knockbackDirection, knockbackForce, hitLaunchDuration);
        }

        /// <summary>
        /// Setzt einen Knockback in die übergebene Richtung, mit eigener Stärke/Dauer -
        /// wird sowohl vom Angriffs-Rückstoß als auch vom Treffer-Knockback (TakeDamage) genutzt.
        /// </summary>
        private void TriggerKnockback(Vector3 direction, float force, float duration, bool isHitLaunch = false)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            _knockbackStartVelocity = direction.normalized * force;
            _knockbackVelocity = _knockbackStartVelocity;
            _knockbackTimer = duration;
            _knockbackDurationTotal = duration;
            _isHitLaunch = isHitLaunch;
            _launchScreenSign = GetScreenSign(direction);
        }

        /// <summary>+1, wenn die Richtung auf dem Bildschirm nach rechts zeigt, sonst -1.</summary>
        private float GetScreenSign(Vector3 direction)
        {
            float screenX = Camera.main != null ? Vector3.Dot(direction, Camera.main.transform.right) : direction.x;
            return screenX >= 0f ? 1f : -1f;
        }

        private void HandleKnockback()
        {
            if (_hitStopTimer > 0f)
            {
                // Kurzer "Treffer-Stopp": eingefroren im Moment des Schlags, dann geht's los.
                _hitStopTimer -= Time.deltaTime;
                return;
            }

            _knockbackTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(_knockbackTimer / _knockbackDurationTotal); // 1 = gerade getroffen, 0 = Knockback vorbei
            // Spieler-Treffer: sehr schneller Start, dann starkes Abbremsen (wirkt wie ein harter Schlag).
            _knockbackVelocity = _knockbackStartVelocity * (_isHitLaunch ? t * t : t);

            Vector3 motion = _knockbackVelocity + Vector3.up * _verticalVelocity.y;
            _controller.Move(motion * Time.deltaTime);
        }

        private void ApplyGravity()
        {
            if (_controller.isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -2f;
            }
            _verticalVelocity.y += gravity * Time.deltaTime;
        }

        private void FaceDirection(Vector3 direction)
        {
            if (visualTransform == null || direction.sqrMagnitude < 0.0001f) return;

            bool movingRight = direction.x > 0f;
            _facingSign = movingRight ? -1f : 1f;
            // Die eigentliche Skalierung (inkl. Spiegelung) setzt UpdateHitReaction in LateUpdate.
        }

        /// <summary>
        /// Zieht dem Gegner Schaden ab (z.B. von PlayerController.UpdateProjectiles bei einem
        /// Projektil-Treffer aufgerufen). hitSourcePosition bestimmt die Knockback-Richtung.
        /// Bei 0 HP wird der Gegner zerstört (tötbar, wie gewünscht - keine visuelle HP-Leiste)
        /// und lässt eine Münze fallen.
        /// </summary>
        public void TakeDamage(int amount, Vector3 hitSourcePosition)
        {
            if (_isDying || _currentHealth <= 0 || amount <= 0) return;
            _currentHealth -= amount;
            ShowDamageText(amount);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayEnemyDamagedSound(transform.position);
            }

            Vector3 knockDirection = transform.position - hitSourcePosition;
            knockDirection.y = 0f;
            float damageRatio = maxHealth > 0 ? Mathf.Clamp01(amount / (float)maxHealth) : 0f;
            float force = hitLaunchForce * (1f + hitDamageForceScaling * damageRatio);

            if (_currentHealth <= 0)
            {
                GameSession.RecordEnemyDefeated();
                if (AchievementManager.Instance != null) AchievementManager.Instance.RecordEnemyDefeated();
                StartDeath(knockDirection, force);
                return;
            }

            TriggerHitReaction();
            TriggerKnockback(knockDirection, force, hitLaunchDuration, true);
        }

        /// <summary>
        /// Erzeugt eine kurz sichtbare Schadenszahl über dem Gegner (3D-Weltraum-Text, kein UI-
        /// Canvas nötig, da die Position pro Gegner unterschiedlich ist). Kein Bild/Textur -
        /// reiner Text, wird direkt zur Laufzeit erzeugt und nach kurzer Zeit automatisch entfernt.
        /// Bewusst NICHT als Kind des Gegners, damit die Zahl auch bei einem Todestreffer
        /// noch kurz sichtbar bleibt, statt sofort mit zerstört zu werden.
        /// </summary>
        private void ShowDamageText(int amount)
        {
            GameObject textObject = new GameObject("DamageText");
            textObject.transform.position = transform.position + Vector3.up * damageTextHeight;

            if (Camera.main != null)
            {
                // Die Kamera dreht sich in diesem Projekt nie, ein einmaliges Ausrichten reicht.
                textObject.transform.rotation = Camera.main.transform.rotation;
            }

            TextMeshPro tmp = textObject.AddComponent<TextMeshPro>();
            tmp.text = amount.ToString();
            tmp.fontSize = damageTextFontSize;
            tmp.color = damageTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;

            _damageTexts.Add(new FloatingDamageText
            {
                Text = tmp,
                StartPosition = textObject.transform.position,
                Drift = new Vector3(Random.Range(-0.6f, 0.6f), 0f, 0f),
                Timer = 0f
            });
        }

        /// <summary>
        /// Schadenszahlen: ploppen groß auf, schrumpfen auf Normalgröße, steigen leicht schräg
        /// nach oben und blenden am Ende aus.
        /// </summary>
        private void UpdateDamageTexts()
        {
            float lifetime = Mathf.Max(0.05f, damageTextLifetime);

            for (int i = _damageTexts.Count - 1; i >= 0; i--)
            {
                FloatingDamageText entry = _damageTexts[i];
                if (entry.Text == null)
                {
                    _damageTexts.RemoveAt(i);
                    continue;
                }

                entry.Timer += Time.deltaTime;
                float t = Mathf.Clamp01(entry.Timer / lifetime);

                float scale = t < 0.15f ? Mathf.Lerp(0.5f, 1.6f, t / 0.15f) : Mathf.Lerp(1.6f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f));
                entry.Text.transform.localScale = Vector3.one * scale;
                entry.Text.transform.position = entry.StartPosition + Vector3.up * (t * 1.2f) + entry.Drift * t;

                Color color = entry.Text.color;
                color.a = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
                entry.Text.color = color;

                if (t >= 1f)
                {
                    Destroy(entry.Text.gameObject);
                    _damageTexts.RemoveAt(i);
                }
            }
        }

        private void OnDestroy()
        {
            // Falls der Gegner anders entfernt wird (Szenenwechsel), keine Schadenszahlen zurücklassen.
            foreach (FloatingDamageText entry in _damageTexts)
            {
                if (entry.Text != null) Destroy(entry.Text.gameObject);
            }
        }
    }
}
