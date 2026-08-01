using System.Collections.Generic;

namespace MP_MeowOnlineShop
{
    internal sealed class MapEventWakeRegistry
    {
        private readonly Dictionary<int, int> wakeUntilByMapId = new Dictionary<int, int>();

        public int Count => wakeUntilByMapId.Count;

        public void Clear() => wakeUntilByMapId.Clear();

        public void Extend(int mapId, int untilTick)
        {
            int prev;
            if (wakeUntilByMapId.TryGetValue(mapId, out prev))
            {
                if (untilTick > prev)
                    wakeUntilByMapId[mapId] = untilTick;
                return;
            }

            wakeUntilByMapId[mapId] = untilTick;
        }

        public bool IsAwake(int mapId, int tick)
        {
            int until;
            return wakeUntilByMapId.TryGetValue(mapId, out until) && until > tick;
        }

        public int GetWakeUntil(int mapId)
        {
            int until;
            return wakeUntilByMapId.TryGetValue(mapId, out until) ? until : int.MinValue;
        }

        public void RemoveExpired(int tick)
        {
            if (wakeUntilByMapId.Count == 0)
                return;

            var expired = new List<int>();
            foreach (var kv in wakeUntilByMapId)
            {
                if (kv.Value <= tick)
                    expired.Add(kv.Key);
            }

            for (int i = 0; i < expired.Count; i++)
                wakeUntilByMapId.Remove(expired[i]);
        }
    }
}
