using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>
    /// The worlds a player is shown: three, polished, instead of eight. The others still exist in the kit (and
    /// open by name), but the map, the dock's Next, and "surprise me" stay within these.
    /// </summary>
    public static class Featured
    {
        public static readonly string[] Ids = { "campfire", "space", "meadow" };

        public static BiomeDef[] Defs
        {
            get
            {
                var list = new List<BiomeDef>();
                foreach (var id in Ids) { var d = Kit.Get(id); if (d != null) list.Add(d); }
                return list.ToArray();
            }
        }

        public static bool Has(string id) => System.Array.IndexOf(Ids, id) >= 0;
    }
}
