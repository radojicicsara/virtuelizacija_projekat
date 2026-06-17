using System;
using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class TransferNotification
    {
        [DataMember]
        public string EventType { get; set; }

        [DataMember]
        public string Message { get; set; }

        [DataMember]
        public string BatteryId { get; set; }

        [DataMember]
        public string TestId { get; set; }

        [DataMember]
        public int SoC { get; set; }

        [DataMember]
        public int RowIndex { get; set; }

        [DataMember]
        public double FrequencyHz { get; set; }

        [DataMember]
        public double Q { get; set; }

        [DataMember]
        public double ReferenceQ { get; set; }

        [DataMember]
        public double DeltaPhi { get; set; }

        [DataMember]
        public DateTime Timestamp { get; set; }
    }
}
