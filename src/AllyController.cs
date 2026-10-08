using System;
using System.Collections.Generic;
using UnityEngine;

namespace AllyOnKill
{
    /// <summary>
    /// この MOD の心臓部。
    /// ・敵が死んだ時に「味方コピー」をその場に生やす
    /// ・毎 tick に追従・戦闘・デスポーン抑止を行う
    /// ・G キーで追従トグル
    /// </summary>
    public static class AllyController
    {
        private const string TickKey = "Tick";
        private const string UpdateKey = "AllyUpdate";
        private const string ConvertKey = "Convert";

        private static float _nextTick;
        private static readonly List<Entity> _scratch = new List<Entity>(64);

        private static AllySettings S => Safe.Settings;

        // ==================================================================
        // 生成フック
        // ==================================================================

        /// <summary>
        /// EntityAlive.OnEntityDeath の後に呼ばれます（＝倒れた瞬間）。
        /// 元の個体はそのまま死なせて（戦利品・経験値・クエストは通常通り）、
        /// 同じエンティティクラスの新しい個体を「味方」としてその場に生成します。
        /// </summary>
        public static void OnEntityDeath(EntityAlive victim)
        {
            if (victim == null) return;

            // 味方自身が死んだときは登録から外すだけ
            if (AllyRegistry.IsAlly(victim))
            {
                AllyRegistry.Remove(victim.entityId);
                return;
            }

            if (!S.ConvertOnKill) return;
            if (Safe.Tripped(ConvertKey)) return;

            try
            {
                Convert(victim);
                Safe.Success(ConvertKey);
            }
            catch (Exception e)
            {
                Safe.OnError(ConvertKey, e.Message);
            }
        }

        /// <summary>倒れた敵を味方として蘇らせる本体</summary>
        private static void Convert(EntityAlive victim)
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            if (victim.isEntityRemote)
                return;

            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            if (world == null || player == null) return;

            if (!IsConvertible(victim, player))
                return;

            // 味方が倒した敵は、既定では味方にしない（連鎖増殖を防ぐ）
            if (!S.ConvertOnAllyKill && RecentlyHitByAlly(victim.entityId))
            {
                LogMsg("味方が倒した敵なので味方化をスキップ: " + victim.EntityName);
                return;
            }

            // 上限チェック
            if (AllyRegistry.Count >= S.MaxAllies)
            {
                LogMsg("味方が上限(" + S.MaxAllies + ")に達したため味方化をスキップ");
                return;
            }

            // 確率
            if (S.ConvertChance < 1f && UnityEngine.Random.value > S.ConvertChance)
                return;

            var ally = SpawnAllyCopy(victim, player);
            if (ally == null) return;

            LogMsg(victim.EntityName + " を味方にしました (id=" + ally.entityId + ")");

            if (S.ShowMessages)
            {
                try { GameManager.ShowTooltip(player, "味方になった: " + victim.EntityName); }
                catch (Exception e) { Log.Warning("[AllyOnKill] ShowTooltip 失敗: " + e.Message); }
            }

            // 死体を消す（味方として蘇ったので、倒れたままの死体は邪魔）
            if (S.RemoveCorpse)
            {
                try { victim.ForceDespawn(); }
                catch (Exception e) { Log.Warning("[AllyOnKill] ForceDespawn 失敗: " + e.Message); }
            }
        }

        /// <summary>
        /// EntityAlive.PostInit の後に呼ばれます。
        /// ワールド再読み込みなどでエンティティが再生成された時に、
        /// cvar の味方マーカーを見て自動的に再登録します。
        /// </summary>
        public static void OnPostInit(EntityAlive entity)
        {
            if (entity == null) return;
            if (AllyRegistry.IsAlly(entity)) return;
            if (!AllyRegistry.HasAllyMarker(entity)) return;

            var world = GameManager.Instance?.World;
            var player = world?.GetPrimaryPlayer();
            AllyRegistry.Mark(entity, player);
            LogMsg("ワールド再読み込みに伴い味方を再登録: " + entity.EntityName + " (id=" + entity.entityId + ")");
        }

        // ==================================================================
        // 毎フレーム
        // ==================================================================

        public static void Tick()
        {
            // 例外が続いた場合は更新処理そのものを止める（ログ荒れ / UI 停止を防ぐ）
            if (Safe.Tripped(TickKey)) return;

            try
            {
                HandleKeys();

                if (Time.time < _nextTick) return;
                _nextTick = Time.time + Mathf.Max(0.05f, S.TickInterval);

                var world = GameManager.Instance?.World;
                var player = world?.GetPrimaryPlayer();
                if (world == null || player == null || player.IsDead()) return;

                foreach (var ally in AllyRegistry.All())
                {
                    try
                    {
                        UpdateOne(ally, player);
                    }
                    catch (Exception e)
                    {
                        if (Safe.OnError(UpdateKey, e.Message)) return;
                    }
                }

                Safe.Success(TickKey);
            }
            catch (Exception e)
            {
                Safe.OnError(TickKey, e.Message);
            }
        }

        private static void UpdateOne(EntityAlive ally, EntityPlayer player)
        {
            // 1) デスポーン抑止の再確認（スポーン元が書き換わっている事がある）
            if (ally.GetSpawnerSource() != EnumSpawnerSource.StaticSpawner)
                ally.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);

            // 2) 誤ってプレイヤー/味方を狙っていたら解除
            var target = ally.GetAttackTarget();
            if (target != null && IsProtected(target))
            {
                ally.SetAttackTarget(null, 0);
                ally.SetRevengeTarget(null);
                target = null;
            }

            // 3) 周囲の敵を探す
            EntityAlive hostile = null;
            if (S.AssistPlayer)
                hostile = FindHostile(ally, S.AssistRadius);

            if (hostile == null)
            {
                // 4) 敵がいなければ、プレイヤーの近くの敵を AI に教えておく
                if (S.AssistPlayer && (target == null || target.IsDead()))
                    AssignTarget(ally);

                // 5) 追従
                if (AllyRegistry.Following)
                    UpdateFollow(ally, player);

                return;
            }

            // --- 敵がいる場合 ---

            // 6) AI にも一応指示する（効かないことが多いが、近づく動作が出る場合がある）
            if (S.AssistPlayer)
                AssignTargetTo(ally, hostile);

            // 7) 直接ダメージで戦わせる（本命）
            if (S.DirectAssist)
                DirectAssist(ally, hostile);

            // 7.5) 敵側も味方を襲う（相互に敵対）
            if (S.EnemyAttackAlly)
                EnemyStrikeBack(hostile, ally);

            // 8) 敵に近づく（追従より優先）
            if (S.EngageEnemies)
                MoveTo(ally, hostile.position);
            else if (AllyRegistry.Following)
                UpdateFollow(ally, player);
        }

        /// <summary>指定位置へ移動させます</summary>
        private static void MoveTo(EntityAlive ally, Vector3 pos)
        {
            try { ally.moveHelper.SetMoveTo(pos, false); }
            catch (Exception e) { Log.Warning("[AllyOnKill] SetMoveTo 失敗: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // 直接攻撃
        // ------------------------------------------------------------------

        /// <summary>味方ごとの次回攻撃時刻</summary>
        private static readonly Dictionary<int, float> NextAttack = new Dictionary<int, float>();

        /// <summary>味方から攻撃された敵（entityId → 最終被弾時刻）。連鎖味方化の抑制に使う</summary>
        private static readonly Dictionary<int, float> AllyHits = new Dictionary<int, float>();

        /// <summary>
        /// AI に頼らず、味方の近くの敵に直接ダメージを与えます。
        /// 7DTD のゾンビ AI はゾンビを攻撃対象にしないため、これが本体です。
        /// </summary>
        private static void DirectAssist(EntityAlive ally, EntityAlive enemy)
        {
            if (!Combat.Available) return;
            if (enemy == null) return;

            // 攻撃範囲内かどうかを確認
            var range = Mathf.Max(0.5f, S.DirectAssistRange);
            if ((enemy.position - ally.position).sqrMagnitude > range * range) return;

            float next;
            if (NextAttack.TryGetValue(ally.entityId, out next) && Time.time < next) return;
            NextAttack[ally.entityId] = Time.time + Mathf.Max(0.1f, S.DirectAssistInterval);

            // 狙った敵の方を向かせる（見た目をそれらしく）
            try { ally.SetLookPosition(enemy.position); }
            catch { /* 無視 */ }

            if (!Combat.Hurt(ally, enemy, S.DirectAssistDamage)) return;

            AllyHits[enemy.entityId] = Time.time;

            // 敵に反撃させる（AI が対応していれば効く）
            if (S.TauntEnemies)
            {
                try
                {
                    enemy.SetAttackTarget(ally, 600);
                    enemy.SetRevengeTarget(ally);
                }
                catch { /* 無視 */ }
            }

            LogMsg(ally.EntityName + " → " + enemy.EntityName + " に " + S.DirectAssistDamage + " ダメージ");
        }

        /// <summary>敵側の次回攻撃時刻</summary>
        private static readonly Dictionary<int, float> NextEnemyAttack = new Dictionary<int, float>();

        /// <summary>
        /// 敵ゾンビが味方ゾンビへ向かい、直接ダメージを与えます。
        /// プレイヤーを狙っている敵は移動だけ邪魔しません（射程内なら殴る）。
        /// </summary>
        private static void EnemyStrikeBack(EntityAlive enemy, EntityAlive ally)
        {
            if (enemy == null || ally == null || enemy.IsDead() || ally.IsDead()) return;

            var t = enemy.GetAttackTarget();
            if (!(t is EntityPlayer))
                MoveTo(enemy, ally.position);

            if (!Combat.Available) return;

            var range = Mathf.Max(0.5f, S.DirectAssistRange);
            if ((enemy.position - ally.position).sqrMagnitude > range * range) return;

            float next;
            if (NextEnemyAttack.TryGetValue(enemy.entityId, out next) && Time.time < next) return;
            NextEnemyAttack[enemy.entityId] = Time.time + Mathf.Max(0.1f, S.DirectAssistInterval);

            try { enemy.SetLookPosition(ally.position); } catch { /* 無視 */ }

            Combat.Hurt(enemy, ally, S.DirectAssistDamage);
        }

        /// <summary>
        /// 直近に味方から攻撃された敵かどうか（= 味方が倒した可能性が高い）
        /// </summary>
        private static bool RecentlyHitByAlly(int entityId)
        {
            float t;
            if (!AllyHits.TryGetValue(entityId, out t)) return false;

            // 5秒以上前なら関係ないとみなして削除
            if (Time.time - t > 5f)
            {
                AllyHits.Remove(entityId);
                return false;
            }
            return true;
        }

        private static void UpdateFollow(EntityAlive ally, EntityPlayer player)
        {
            var dist = Vector3.Distance(ally.position, player.position);

            // 遠すぎる → テレポート（デスポーン/置き去り対策）
            if (dist > S.TeleportDistance)
            {
                TeleportToPlayer(ally, player);
                return;
            }

            if (dist <= S.FollowDistance) return;

            // バニラの「調査」AI を利用してプレイヤーへ向かわせる
            try
            {
                ally.SetInvestigatePosition(player.position, 200);
            }
            catch
            {
                // 無視
            }

            // 併せて移動ヘルパーにも直接目的地を渡す（AI タスク構成に依存しない保険）
            try
            {
                if (ally.moveHelper != null)
                    ally.moveHelper.SetMoveTo(player.position, false);
            }
            catch
            {
                // 無視
            }
        }

        // ==================================================================
        // キー入力
        // ==================================================================

        private static void HandleKeys()
        {
            var world = GameManager.Instance?.World;
            if (world == null || world.GetPrimaryPlayer() == null) return;

            var player = world.GetPrimaryPlayer();
            if (player == null) return;

            if (KeyInput.GetKeyDown(S.FollowKey))
            {
                AllyRegistry.Following = !AllyRegistry.Following;
                var msg = AllyRegistry.Following ? "味方の追従: ON" : "味方の追従: OFF";
                Log.Out("[AllyOnKill] " + msg);
                if (S.ShowMessages) Show(player, msg + "  (味方 " + AllyRegistry.Count + " 体)");
            }

            if (S.DismissKey != KeyCode.None && KeyInput.GetKeyDown(S.DismissKey))
            {
                DismissAll();
                if (S.ShowMessages) Show(player, "味方を全員解放しました");
            }
        }

        /// <summary>画面にツールチップを出す（失敗してもログを荒らさない）</summary>
        private static void Show(EntityPlayerLocal player, string msg)
        {
            try { GameManager.ShowTooltip(player, msg); }
            catch (Exception e) { Log.Warning("[AllyOnKill] ShowTooltip 失敗: " + e.Message); }
        }

        public static void DismissAll()
        {
            // 列挙中に登録解除するとコレクションが壊れるので、先にリスト化する
            var list = new List<EntityAlive>(AllyRegistry.All());
            foreach (var ally in list)
                AllyRegistry.Unmark(ally);
            AllyRegistry.Clear();
        }

        // ==================================================================
        // 内部ヘルパー
        // ==================================================================

        /// <summary>味方化してよい相手かどうか</summary>
        private static bool IsConvertible(EntityAlive victim, EntityPlayer player)
        {
            if (victim == null || victim.IsDead() == false) return false;

            // プレイヤー / 味方 / 車両などは除外
            if (victim is EntityPlayer) return false;
            if (AllyRegistry.IsAlly(victim)) return false;
            if (!(victim is EntityEnemy) && !(S.AttackAnimals && victim is EntityAnimal))
                return false;

            // 距離
            if (S.ConvertRadius > 0f)
            {
                var d = Vector3.Distance(victim.position, player.position);
                if (d > S.ConvertRadius) return false;
            }

            // 名前のフィルタ
            if (S.Blacklist != null && S.Blacklist.Count > 0)
            {
                var name = (victim.EntityName ?? string.Empty).ToLowerInvariant();
                var cls = EntityNames.ClassNameOf(victim).ToLowerInvariant();
                var hit = false;
                foreach (var needle in S.Blacklist)
                {
                    if (name.Contains(needle) || cls.Contains(needle)) { hit = true; break; }
                }

                // ホワイトリストとして使う場合は「一致したものだけ」許可
                if (S.BlacklistIsWhitelist && !hit) return false;
                if (!S.BlacklistIsWhitelist && hit) return false;
            }

            return true;
        }

        /// <summary>倒れた敵と同じエンティティクラスの個体を、味方として生成する</summary>
        private static EntityAlive SpawnAllyCopy(EntityAlive victim, EntityPlayer player)
        {
            var world = GameManager.Instance.World;

            EntityAlive ally;
            try
            {
                ally = EntityFactory.CreateEntity(victim.entityClass, victim.position) as EntityAlive;
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] EntityFactory.CreateEntity 失敗: " + e);
                return null;
            }

            if (ally == null)
            {
                Log.Warning("[AllyOnKill] エンティティ生成に失敗 (class=" + victim.entityClass + ")");
                return null;
            }

            // 生成直後はこちらを向いているので、とりあえずターゲットを消す
            try
            {
                ally.SetAttackTarget(null, 0);
                ally.SetRevengeTarget(null);
            }
            catch
            {
                // 無視
            }

            AllyRegistry.Mark(ally, player);

            try
            {
                world.SpawnEntityInWorld(ally);
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] SpawnEntityInWorld 失敗: " + e);
                AllyRegistry.Remove(ally.entityId);   // 登録だけ残さないように後始末
                return null;
            }

            return ally;
        }

        /// <summary>守るべき相手（プレイヤー・他の味方）か</summary>
        public static bool IsProtected(EntityAlive other)
        {
            if (other == null) return false;
            if (other is EntityPlayer) return true;
            return AllyRegistry.IsAlly(other);
        }

        /// <summary>近くの敵を探して攻撃対象にする</summary>
        private static void AssignTarget(EntityAlive ally)
        {
            AssignTargetTo(ally, FindHostile(ally, S.AssistRadius));
        }

        /// <summary>指定した敵を攻撃対象にする</summary>
        private static void AssignTargetTo(EntityAlive ally, EntityAlive best)
        {
            if (best == null) return;

            try { ally.SetAttackTarget(best, 600); }
            catch (Exception e) { Log.Warning("[AllyOnKill] SetAttackTarget 失敗: " + e.Message); }
        }

        /// <summary>
        /// 味方の近くにいる「敵」を1体探します。
        /// プレイヤー・他の味方は除外します。
        /// </summary>
        private static EntityAlive FindHostile(EntityAlive ally, float radius)
        {
            var world = GameManager.Instance.World;
            if (world == null) return null;

            radius = Mathf.Max(1f, radius);

            _scratch.Clear();
            try
            {
                world.GetEntitiesInBounds(typeof(EntityAlive),
                    new Bounds(ally.position, Vector3.one * radius * 2f), _scratch);
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] GetEntitiesInBounds 失敗: " + e.Message);
                return null;
            }

            EntityAlive best = null;
            var bestDist = float.MaxValue;
            var rangeSq = radius * radius;

            for (var i = 0; i < _scratch.Count; i++)
            {
                var candidate = _scratch[i] as EntityAlive;
                if (candidate == null || candidate == ally) continue;
                if (candidate.IsDead()) continue;
                if (IsProtected(candidate)) continue;

                var isHostile = candidate is EntityEnemy || (S.AttackAnimals && candidate is EntityAnimal);
                if (!isHostile) continue;

                var d = (candidate.position - ally.position).sqrMagnitude;
                if (d > rangeSq) continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>プレイヤーの足元付近にテレポートさせる</summary>
        private static void TeleportToPlayer(EntityAlive ally, EntityPlayer player)
        {
            var world = GameManager.Instance.World;

            var pos = player.position;
            try
            {
                // プレイヤーが通ってきた安全な場所を優先
                var breadcrumb = player.GetBreadcrumbPos(UnityEngine.Random.Range(0.5f, 3f));
                if (breadcrumb.y > 0f) pos = breadcrumb;
            }
            catch
            {
                // 無視（プレイヤー位置のまま）
            }

            pos.y = SurfaceY(world, pos.x, pos.z) + 0.1f;

            try
            {
                ally.motion = Vector3.zero;
                ally.SetPosition(pos, true);
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] テレポート失敗: " + e.Message);
                return;
            }

            LogMsg(ally.EntityName + " をプレイヤー付近にテレポートさせました");
        }

        /// <summary>
        /// (x, z) の地表の高さを返します。
        /// 地形だけでなく、その上に置かれたブロック（拠点の床など）も考慮します。
        /// </summary>
        private static float SurfaceY(World world, float x, float z)
        {
            try
            {
                var y = (int)world.GetHeightAt(x, z);
                var bx = (int)x;
                var bz = (int)z;
                var cap = y + 20;
                while (y < cap && world.GetBlock(bx, y + 1, bz).Block.shape.IsSolidSpace)
                    y++;
                return y + 1;
            }
            catch
            {
                return world.GetHeightAt(x, z) + 1f;
            }
        }

        private static void LogMsg(string msg)
        {
            if (S != null && S.DebugLog)
                Log.Out("[AllyOnKill] " + msg);
        }
    }
}
