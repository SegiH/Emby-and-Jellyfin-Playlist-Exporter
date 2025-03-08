using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp;

namespace Jellyfin_Playlist_Exporter.Models
{
    public class RestResult

    {
        public string Status { get; set; }
        public IRestResponse Response { get; set; }
        public string ErrorMessage { get; set; }

        public static implicit operator RestResponse(RestResult v)
        {
            throw new NotImplementedException();
        }
    }
}
