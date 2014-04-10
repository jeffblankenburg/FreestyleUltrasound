using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace FreestyleUltrasound
{
    [DataContract]
    public class Device
    {
        [DataMember]
        public string Name { get; set; }
        [DataMember]
        public string IPAddress { get; set; }
        [DataMember]
        public string PIN { get; set; }
        [DataMember]
        public string GUID { get; set; }
    }
}
