using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel;

namespace Common
{
    [ServiceContract] 
    public interface IBatteryService
    {
        [OperationContract]
        string StartSession(EisMeta meta);


        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        void PushSample(EisSample sample);

        [OperationContract]
        string EndSession();
    }
}
