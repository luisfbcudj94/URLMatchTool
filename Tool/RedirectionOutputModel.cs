using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool
{
    public class RedirectionOutputModel
    {
        public int Index { get; set; }
        public string RedirectionURL { get; set; }
        public string DestinationURL { get; set; }
        public string DestinationDomain { get; set; }
        public string FinalDomain { get; set; }
        public string Status { get; set; }
        public string StatusCode { get; set; }
        public string FinalUrl { get; set; }
        public string Redirects { get; set; }
    }
}
