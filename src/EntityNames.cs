using System;
using System.Reflection;

namespace AllyOnKill
{
    /// <summary>
    /// エンティティクラス名（entityclasses.xml の name= の値）を取得します。
    /// EntityClass のフィールド名はバージョンで変わる可能性があるため、
    /// リフレクションで探し、取れなければ空文字を返します（＝名前フィルタだけ無効化）。
    /// </summary>
    internal static class EntityNames
    {
        private static FieldInfo _nameField;
        private static bool _searched;

        public static string ClassNameOf(EntityAlive entity)
        {
            if (entity == null) return string.Empty;

            try
            {
                var list = EntityClass.list;
                if (list == null) return string.Empty;
                if (!list.ContainsKey(entity.entityClass)) return string.Empty;

                var ec = list[entity.entityClass];
                if (ec == null) return string.Empty;

                if (!_searched)
                {
                    _searched = true;
                    foreach (var f in ec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (f.FieldType != typeof(string)) continue;
                        if (f.Name.IndexOf("class", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        _nameField = f;
                        break;
                    }
                }

                return _nameField != null ? (_nameField.GetValue(ec) as string ?? string.Empty) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
