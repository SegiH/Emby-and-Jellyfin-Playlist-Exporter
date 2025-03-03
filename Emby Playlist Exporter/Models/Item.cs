using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmbyJellyfin_Playlist_Exporter {
    public class Item {
        public string Name { get; set; }
        public string ServerId { get; set; }
        public string Id { get; set; }
        public object[] Artists { get; set; }
        public long RunTimeTicks { get; set; }
        public bool IsFolder { get; set; }
        public string Type { get; set; }
        public Imagetags ImageTags { get; set; }
        public object[] BackdropImageTags { get; set; }
        public string MediaType { get; set; }
        public string OfficialRating { get; set; }
        public string Path { get; set; }
        public Playlists PlaylistTracks { get; set; }
    }
}
