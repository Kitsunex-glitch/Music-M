using System;
using static VK_UI3.Views.SectionView;

namespace VK_UI3.Views.Tabs
{
    /// <summary>
    /// Ключ содержимого вкладки. Используется для дедупликации: клик по тому же
    /// контенту без Ctrl активирует уже открытую вкладку, а не создаёт новую.
    /// </summary>
    public sealed class TabKey : IEquatable<TabKey>
    {
        public SectionType Type { get; }
        public string Id { get; }

        public TabKey(SectionType type, string id)
        {
            Type = type;
            Id = Normalize(type, id);
        }

        private static string Normalize(SectionType type, string id)
        {
            // Для плейлистов/альбомов порядок частей важен: owner_id_album_id[_accessKey]
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        public bool Equals(TabKey other)
        {
            if (other is null) return false;
            return Type == other.Type && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as TabKey);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Type * 397) ^ (Id?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() => $"{Type}|{Id}";
    }
}
