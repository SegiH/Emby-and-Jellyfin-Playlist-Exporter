using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmbyJellyfin_Playlist_Exporter {
    // Classes that hold JSON object payload
    public class Playlists {
        public Item[] Items { get; set; }
        public int TotalRecordCount { get; set; }
    }
}
