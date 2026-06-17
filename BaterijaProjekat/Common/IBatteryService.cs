using System.ServiceModel;

namespace Common
{
    [ServiceContract(CallbackContract = typeof(IBatteryServiceCallback), SessionMode = SessionMode.Required)]
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
