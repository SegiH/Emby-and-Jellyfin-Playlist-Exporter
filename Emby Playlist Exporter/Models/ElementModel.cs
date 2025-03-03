using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin_Playlist_Exporter.Models
{
    public class ElementModel
    {
        public string ElementName { get; set; }
       
        public int Top { get; set; }

        public int Left { get; set; }
    }
}
