using System;
using System.Collections.Generic;

namespace CoffeeShop.Seating
{
    /// <summary>
    /// 单个座位的占用状态（FR-SEAT-02）。纯状态机，不依赖场景、不依赖 UnityEngine，
    /// 因此可以在 Edit Mode 里直接构造、直接断言。
    ///
    /// 规则：
    ///   - 空字符串 / null 不能作为占用者（否则等于没占）
    ///   - 已占用时再次占用一律失败（这就是"客人不抢已占用座位"的底层保障）
    ///   - 释放必须由当前占用者发起，避免 A 客人离开顺手把 B 客人的座放掉
    /// </summary>
    public class SeatState
    {
        public string Id { get; }
        public string Zone { get; }
        public SeatKind Kind { get; }
        public string Occupant { get; private set; }

        public bool IsFree => string.IsNullOrEmpty(Occupant);
        public bool IsOccupied => !IsFree;

        public SeatState(string id, string zone, SeatKind kind)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("座位 id 不能为空", nameof(id));
            Id = id;
            Zone = zone ?? string.Empty;
            Kind = kind;
        }

        /// <summary>占用。成功返回 true；已占用或占用者非法返回 false。</summary>
        public bool TryOccupy(string occupantId)
        {
            if (string.IsNullOrWhiteSpace(occupantId)) return false;
            if (IsOccupied) return false;
            Occupant = occupantId;
            return true;
        }

        /// <summary>释放。只有当前占用者本人能释放；座位本来就空时也返回 false（不假装成功）。</summary>
        public bool Release(string occupantId)
        {
            if (IsFree) return false;
            if (!string.Equals(Occupant, occupantId, StringComparison.Ordinal)) return false;
            Occupant = null;
            return true;
        }

        public override string ToString() => $"{Id}[{Zone}] {(IsFree ? "free" : "by " + Occupant)}";
    }

    /// <summary>
    /// 座位集合。对外只暴露"按 id 占用 / 释放 / 查询"，是 05 票客人找座位的唯一入口。
    /// </summary>
    public class SeatRegistry
    {
        readonly List<SeatState> seats = new List<SeatState>();
        readonly Dictionary<string, SeatState> byId = new Dictionary<string, SeatState>(StringComparer.Ordinal);

        public IReadOnlyList<SeatState> All => seats;
        public int Count => seats.Count;

        public int FreeCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < seats.Count; i++) if (seats[i].IsFree) n++;
                return n;
            }
        }

        public int OccupiedCount => Count - FreeCount;

        /// <summary>加入座位。id 重复会抛异常——配置写错应当当场炸，而不是悄悄覆盖。</summary>
        public void Add(SeatState seat)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            if (byId.ContainsKey(seat.Id))
                throw new InvalidOperationException($"座位 id 重复: {seat.Id}");
            seats.Add(seat);
            byId.Add(seat.Id, seat);
        }

        public SeatState Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return byId.TryGetValue(id, out var s) ? s : null;
        }

        public bool TryOccupy(string id, string occupantId)
        {
            var s = Find(id);
            return s != null && s.TryOccupy(occupantId);
        }

        public bool Release(string id, string occupantId)
        {
            var s = Find(id);
            return s != null && s.Release(occupantId);
        }

        /// <summary>把所有人的占用清空（场景重置 / 测试收尾用）。</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i].IsOccupied) seats[i].Release(seats[i].Occupant);
            }
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"seats={Count} free={FreeCount} occupied={OccupiedCount}");
            return sb.ToString();
        }
    }
}
