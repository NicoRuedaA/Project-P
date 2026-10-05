using System.Collections.Generic;
using UnityEngine;
using Wildbound.Combat;

namespace Wildbound
{
    [RequireComponent(typeof(CharacterController))]
    public class Creature : MonoBehaviour
    {
        public int species;
        [Tooltip("Friendly scene actor. Press E to control the nearest available Ally; press E again to return to the trainer without removing this actor.")]
        public bool ally;
        public bool dead, capturing;
        public float health = 100;
        public CreatureRecord record;
        public Transform model;
        public Creature forcedTarget;
        public float stunUntil;
        [Tooltip("Optional per-creature attack override. Register its stable ID in the combat catalog to preserve it when captured.")]
        public CompanionLoadout loadout;
        public float moveSpeed = 5, sprintSpeed = 7;
        public readonly float[] LocalCooldowns = new float[4];
        CharacterController cc;
        Vector3 home, wander, modelRestScale = Vector3.one;
        float nextWander, nextAttack, castAt;
        int queued = -1;
        Vector3 castPoint;
        Creature castTarget;
        CompanionAttack castAttack;
        CompanionAttackVfx castVfx;
        readonly List<CompanionAttackVfx> effects = new List<CompanionAttackVfx>();
        public bool IsCasting => queued >= 0;
        public float MaxHealth => 100 + (record != null ? (record.level - 1) * 12 : 0);
        public CompanionAttack AttackAt(int slot) => loadout ? loadout.AttackAt(slot) : CompanionAttack.CompatibilityDefault(slot);
        public float ReadyAt(int slot) => slot >= 0 && slot < 4 ? (Game.Instance ? Game.Instance.Cooldowns(this) : LocalCooldowns)[slot] : float.PositiveInfinity;

        public void Init(int type, bool friendly, CreatureRecord data = null)
        {
            species = type; ally = friendly; record = data; health = data != null ? data.health : 100; BindRuntime();
        }
        public void BindRuntime()
        {
            cc = GetComponent<CharacterController>(); home = transform.position; wander = home;
            if (model) modelRestScale = model.localScale;
            if (!loadout && Game.Instance) loadout = Game.Instance.ResolveLoadout(record, species);
            if (record != null && loadout) record.combatLoadoutId = loadout.id;
        }

        void Update()
        {
            var game = Game.Instance;
            if (!game || !game.isActiveAndEnabled || dead || capturing || game.paused) return;
            if (Time.time < stunUntil) { CancelCast(); MoveDown(); return; }
            if (queued >= 0 && Time.time >= castAt)
            {
                var attack = castAttack; var point = castPoint; var target = castTarget;
                queued = -1; castAttack = null; castTarget = null; castVfx = null;
                Execute(attack, point, target);
            }
            if (model) model.localScale = modelRestScale * (IsCasting ? 1.07f : 1);
            if (game.IsPossessing && game.Control.Actor == this) { ManualMovement(game); return; }
            if (IsCasting) { MoveDown(); return; }

            Vector3 destination = transform.position;
            Creature enemy = null;
            var controlled = game.ControlledTransform;
            float distanceToPlayer = Vector3.Distance(transform.position, controlled.position);
            if (ally)
            {
                enemy = forcedTarget;
                if (!enemy || enemy.dead || enemy.ally || enemy.capturing) enemy = null;
                destination = game.player.transform.position - transform.right * 2;
                if (enemy) destination = enemy.transform.position;
            }
            else
            {
                enemy = game.companion;
                if (distanceToPlayer < 13 && species != 0) destination = enemy && !enemy.dead ? enemy.transform.position : controlled.position;
                else if (distanceToPlayer < 5 && species == 0 && health > 55)
                {
                    destination = transform.position + (transform.position - controlled.position).normalized * 4;
                    enemy = null;
                }
                else
                {
                    if (Time.time > nextWander) { nextWander = Time.time + Random.Range(3, 7); wander = home + new Vector3(Random.Range(-7, 7), 0, Random.Range(-7, 7)); }
                    destination = wander; enemy = null;
                }
            }
            Vector3 direction = destination - transform.position; direction.y = 0;
            float distance = direction.magnitude;
            bool fighting = ally ? enemy != null : species != 0 && distanceToPlayer < 13;
            if (distance > (fighting ? 5 : ally ? 2 : 1))
            {
                var move = direction.normalized * (ally ? 5 : 3); move.y = -9;
                cc.Move(move * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 6);
            }
            else MoveDown();
            if (fighting && distance < 8 && Time.time > nextAttack) { Begin(0, destination, enemy); nextAttack = Time.time + Random.Range(2.5f, 4); }
        }

        void ManualMovement(Game game)
        {
            var input = game.Control.Input;
            var direction = game.Control.MovementDirection(input ? input.move : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), game.cam ? game.cam.transform : null);
            if (IsCasting) direction = Vector3.zero;
            bool sprint = input ? input.sprint : Input.GetKey(KeyCode.LeftShift);
            var movement = direction * (sprint ? sprintSpeed : moveSpeed);
            movement.y = -9;
            cc.Move(movement * Time.deltaTime);
            if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 12);
            if (transform.position.y < -10) game.Dismiss();
        }
        void MoveDown() { if (cc && cc.enabled) cc.Move(Vector3.down * 9 * Time.deltaTime); }

        public bool Begin(int ability, Vector3 point, Creature enemy)
        {
            var game = Game.Instance; var attack = AttackAt(ability);
            if (!game || game.paused || !isActiveAndEnabled || dead || capturing || IsCasting || Time.time < stunUntil || !attack || !attack.IsValid || Time.time < ReadyAt(ability)) return false;
            if (attack.effect == AttackEffect.Stun && (!enemy || enemy.dead || enemy.capturing || enemy.ally == ally || Vector3.Distance(transform.position, enemy.transform.position) > attack.range)) return false;
            if (enemy && (enemy.dead || enemy.capturing || enemy.ally == ally)) enemy = null;
            if (enemy && attack.effect == AttackEffect.Projectile && Vector3.Distance(transform.position, enemy.transform.position) > attack.range) return false;
            var delta = point - transform.position;
            if (delta.magnitude > attack.range) point = transform.position + delta.normalized * attack.range;
            queued = ability; castAt = Time.time + attack.castDelay; castPoint = point; castTarget = enemy; castAttack = attack;
            game.Cooldowns(this)[ability] = Time.time + attack.cooldown;
            delta = point - transform.position; delta.y = 0;
            if (attack.effect != AttackEffect.Heal && delta.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(delta);
            castVfx = CompanionAttackVfx.Play(attack, this, attack.effect == AttackEffect.Heal ? transform.position : point);
            if (castVfx) { effects.RemoveAll(effect => !effect); effects.Add(castVfx); }
            return true;
        }

        void Execute(CompanionAttack attack, Vector3 point, Creature enemy)
        {
            if (!attack || !attack.IsValid) return;
            var game = Game.Instance;
            float amount = attack.damage + (record != null ? record.level * attack.damagePerLevel : 0);
            if (attack.effect == AttackEffect.Heal) { health = Mathf.Min(MaxHealth, health + amount); if (record != null) record.health = health; return; }
            if (attack.effect == AttackEffect.Area)
            {
                Vector3 center = transform.position + transform.forward * Mathf.Min(2, attack.range);
                foreach (var creature in game.creatures.ToArray())
                    if (creature && creature != this && creature.ally != ally && !creature.dead && !creature.capturing && Vector3.Distance(center, creature.transform.position) < attack.radius) creature.Hurt(amount, this);
                if (!ally && Vector3.Distance(center, game.player.transform.position) < attack.radius) game.player.Hurt(amount);
                return;
            }
            if (attack.effect == AttackEffect.Stun)
            {
                if (enemy && !enemy.dead && !enemy.capturing && enemy.ally != ally && Vector3.Distance(transform.position, enemy.transform.position) <= attack.range)
                { enemy.stunUntil = Time.time + attack.stunDuration; enemy.CancelCast(); }
                return;
            }
            Vector3 origin = transform.position + Vector3.up;
            Vector3 destination = enemy && !enemy.dead && !enemy.capturing ? enemy.transform.position + Vector3.up : point + Vector3.up;
            var direction = destination - origin;
            if (direction.sqrMagnitude > .001f) game.SpawnShot(origin, direction.normalized, this, false, attack);
        }

        public void CancelCast()
        {
            queued = -1; castAttack = null; castTarget = null;
            if (castVfx) castVfx.Cancel(); castVfx = null;
            if (model) model.localScale = modelRestScale;
        }
        public void CancelActions()
        {
            CancelCast();
            foreach (var effect in effects) if (effect) effect.Cancel();
            effects.Clear();
        }
        public void Hurt(float amount, Creature source)
        {
            if (dead || capturing) return;
            health = Mathf.Max(0, health - amount);
            if (!ally && source && source.ally) forcedTarget = source;
            if (Application.isPlaying) Game.Instance.Burst(transform.position + Vector3.up, Color.white, .5f);
            if (health > 0) return;
            dead = true; CancelActions(); cc.enabled = false;
            Game.Instance.Message(Species.Names[species] + " debilitado");
            if (source && source.record != null) source.record.GainXP(25);
            if (ally) { if (record != null) record.health = 0; Game.Instance.ReleaseCompanion(this); }
            if (Application.isPlaying && (!ally || Game.Instance.OwnsCompanion(this))) Destroy(gameObject, ally ? 1 : 18);
        }
        void OnDisable() { CancelActions(); if (Game.Instance) Game.Instance.ReleaseCompanion(this); }
        void OnDestroy() { CancelActions(); if (Game.Instance) { Game.Instance.ReleaseCompanion(this); Game.Instance.creatures.Remove(this); } }
    }
}
