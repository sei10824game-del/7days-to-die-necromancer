using System;
using System.Collections.Generic;

namespace AllyOnKill
{
    /// <summary>
    /// 「現在味方になっているエンティティ」を管理するクラス。
    ///
    /// 登録は 2 段構えです。
    ///   1) 実行中の高速な HashSet（エンティティ ID）
    ///   2) エンティティ本体に入れる cvar「AOK_Ally」（← セーブ/ロードをまたいでも残る）
    /// 2 があるので、ワールドを読み込み直しても PostInit パッチで自動的に再登録されます。
    /// </summary>
    public static class AllyRegistry
    {
        /// <summary>味方マーカーとして使う cvar 名</summary>
        public const string CvarAlly = "AOK_Ally";

        /// <summary>リーダー（プレイヤー）の entityId を入れる cvar 名</summary>
        public const string CvarLeader = "AOK_Leader";

        private static readonly HashSet<int> _ids = new HashSet<int>();

        /// <summary>追従モード（G キーでトグル）</summary>
        public static bool Following = true;

        public static int Count => _ids.Count;

        public static bool Contains(int entityId) => _ids.Contains(entityId);

        /// <summary>
        /// エンティティが味方かどうか。ID の一覧だけで判定するので、
        /// entityId が再利用された場合も誤判定しません（新規エンティティは登録されない）。
        /// </summary>
        public static bool IsAlly(EntityAlive entity)
        {
            return entity != null && _ids.Contains(entity.entityId);
        }

        public static void Add(EntityAlive entity)
        {
            if (entity == null) return;
            _ids.Add(entity.entityId);
        }

        public static void Remove(int entityId)
        {
            _ids.Remove(entityId);
        }

        public static void Clear()
        {
            _ids.Clear();
        }

        /// <summary>
        /// 登録済みの味方を実体として列挙します。
        /// 死んでいる / ワールドから消えているものは、ここで登録解除されます。
        /// </summary>
        public static IEnumerable<EntityAlive> All()
        {
            if (_ids.Count == 0) yield break;

            var world = GameManager.Instance?.World;
            if (world == null) yield break;

            var dead = new List<int>();

            foreach (var id in _ids)
            {
                var e = world.GetEntity(id) as EntityAlive;
                if (e == null || e.IsDead())
                {
                    dead.Add(id);
                    continue;
                }
                yield return e;
            }

            foreach (var id in dead)
                _ids.Remove(id);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// エンティティに味方マーカーを書き込みます。
        /// EnumSpawnerSource.StaticSpawner がデスポーン除外的な働きをするので
        /// （EntityAlive のデスポーン判定はスポーン元を見るため）必ず設定します。
        /// </summary>
        public static void Mark(EntityAlive entity, EntityAlive leader)
        {
            if (entity == null) return;

            try
            {
                entity.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] SetSpawnerSource 失敗: " + e.Message);
            }

            try
            {
                var buffs = entity.Buffs;
                if (buffs != null)
                {
                    SetVar(buffs, CvarAlly, 1f);
                    if (leader != null)
                        SetVar(buffs, CvarLeader, leader.entityId);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] cvar 書き込み失敗: " + e.Message);
            }

            Add(entity);

            // プレイヤーのコンパニオン一覧にも入れるかどうか（既定: 入れない）
            //
            // V3.x の EntityPlayer.Companions は CompanionGroup で、HUD と連動している。
            // ここに生の敵エンティティを入れると UI 側が null 参照を起こし、
            //   [XUi] Error while updating window group 'toolbelt':
            //   Object reference not set to an instance of an object
            // が毎フレーム出てゲームが操作不能になる。そのため既定では無効。
            if (!Safe.Settings.AddToCompanions) return;

            try
            {
                var player = leader as EntityPlayer;
                // 注意: CompanionGroup には Contains が無いので IndexOf で判定する
                if (player != null && player.Companions != null && player.Companions.IndexOf(entity) < 0)
                    player.Companions.Add(entity);
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] Companions への追加に失敗: " + e.Message);
            }
        }

        /// <summary>味方を解放して、普通の敵のデスポーン挙動に戻します</summary>
        public static void Unmark(EntityAlive entity)
        {
            if (entity == null) return;

            Remove(entity.entityId);

            try
            {
                var buffs = entity.Buffs;
                if (buffs != null)
                {
                    SetVar(buffs, CvarAlly, 0f);
                    SetVar(buffs, CvarLeader, -1f);
                }
            }
            catch
            {
                // 無視
            }

            try
            {
                // 通常の敵としてのデスポーン許可を戻す
                entity.SetSpawnerSource(EnumSpawnerSource.Biome);
            }
            catch
            {
                // 無視
            }

            if (!Safe.Settings.AddToCompanions) return;

            try
            {
                var world = GameManager.Instance?.World;
                var player = world?.GetPrimaryPlayer();
                if (player != null && player.Companions != null)
                    player.Companions.Remove(entity);
            }
            catch
            {
                // 無視
            }
        }

        /// <summary>cvar を持っていれば true（ワールド再読み込み後の復元用）</summary>
        public static bool HasAllyMarker(EntityAlive entity)
        {
            try
            {
                var buffs = entity?.Buffs;
                if (buffs == null) return false;
                return buffs.HasCustomVar(CvarAlly) && buffs.GetCustomVar(CvarAlly) > 0f;
            }
            catch
            {
                return false;
            }
        }

        private static void SetVar(EntityBuffs buffs, string name, float value)
        {
            if (buffs.HasCustomVar(name))
                buffs.SetCustomVar(name, value);
            else
                buffs.AddCustomVar(name, value);
        }
    }
}
