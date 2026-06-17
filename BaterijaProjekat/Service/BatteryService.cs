using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceModel;
using Common;

namespace Service
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession)]
    public class BatteryService : IBatteryService, IDisposable
    {
        public static event Action<TransferNotification> OnTransferStarted;
        public static event Action<TransferNotification> OnSampleReceived;
        public static event Action<TransferNotification> OnTransferCompleted;
        public static event Action<TransferNotification> OnWarningRaised;

        private StreamWriter _sessionWriter;
        private StreamWriter _rejectWriter;
        private EisMeta _currentMeta;
        private string _sessionFilePath;
        private string _rejectsFilePath;
        private int _lastRowIndex = -1;
        private int _acceptedSamples;
        private double? _lastPhi;
        private double _qSum;
        private int _qCount;
        private bool _disposed;
        private IBatteryServiceCallback _callback;

        public string StartSession(EisMeta meta)
        {
            if (meta == null)
            {
                throw new ArgumentNullException(nameof(meta));
            }

            CloseSessionFiles();
            ResetSessionMetrics();

            _callback = OperationContext.Current.GetCallbackChannel<IBatteryServiceCallback>();
            _currentMeta = meta;

            string folderPath = Path.Combine("Data", meta.BatteryId, meta.TestId, meta.SoC.ToString());
            Directory.CreateDirectory(folderPath);

            _sessionFilePath = Path.Combine(folderPath, "session.csv");
            _rejectsFilePath = Path.Combine(folderPath, "rejects.csv");

            _sessionWriter = new StreamWriter(_sessionFilePath, append: false);
            _rejectWriter = new StreamWriter(_rejectsFilePath, append: false);

            _sessionWriter.WriteLine("RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm,TimestampLocal,Phi_rad,Q");
            _sessionWriter.Flush();

            _rejectWriter.WriteLine("TimestampLocal,Reason,RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm");
            _rejectWriter.Flush();

            TransferNotification notification = CreateNotification(
                "OnTransferStarted",
                ReadStringSetting("TransferStartedMessage"),
                -1,
                0,
                0,
                0);

            RaiseTransferStarted(notification);
            return $"ACK: Session opened at {_sessionFilePath}";
        }

        public void PushSample(EisSample sample)
        {
            try
            {
                EnsureActiveSession();

                if (sample == null)
                {
                    LogReject("Null sample.", null);
                    throw new FaultException<DataFormatFault>(new DataFormatFault { Message = "Podaci nisu poslati (null)." });
                }

                ValidateSample(sample);

                double phi = Math.Atan2(sample.X_ohm, sample.R_ohm);
                double q = Math.Abs(sample.X_ohm) / sample.R_ohm;
                double baselineQ = _qCount > 0 ? _qSum / _qCount : q;

                AppendAcceptedSample(sample, phi, q);

                TransferNotification sampleNotification = CreateNotification(
                    "OnSampleReceived",
                    ReadStringSetting("SampleReceivedMessage"),
                    sample.RowIndex,
                    sample.FrequencyHz,
                    q,
                    0);

                RaiseSampleReceived(sampleNotification);

                CheckPhaseAngleShift(sample, phi);
                CheckReactiveRatioBounds(sample, q);
                CheckReactiveRatioDeviation(sample, q, baselineQ);

                _lastPhi = phi;
                _qSum += q;
                _qCount++;
                _acceptedSamples++;
            }
            catch (FaultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogReject($"Neočekivana greška: {ex.Message}", sample);
                throw;
            }
        }

        public string EndSession()
        {
            if (_currentMeta == null)
            {
                return "ACK: No active session.";
            }

            string completedSessionPath = _sessionFilePath;
            TransferNotification notification = CreateNotification(
                "OnTransferCompleted",
                $"{ReadStringSetting("TransferCompletedMessage")} Prihvaćeno uzoraka: {_acceptedSamples}.",
                _acceptedSamples > 0 ? _lastRowIndex : -1,
                0,
                _qCount > 0 ? _qSum / _qCount : 0,
                0);

            RaiseTransferCompleted(notification);
            CloseSessionFiles();
            ResetSessionMetrics();

            return $"ACK: Session completed. Saved to {completedSessionPath}";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~BatteryService()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    CloseSessionFiles();
                    ResetSessionMetrics();
                }

                _disposed = true;
            }
        }

        private void ValidateSample(EisSample sample)
        {
            if (sample.RowIndex <= _lastRowIndex)
            {
                string reason = $"RowIndex mora monotono rasti. Poslednji je bio {_lastRowIndex}, a stigao je {sample.RowIndex}.";
                LogReject(reason, sample);
                throw new FaultException<ValidationFault>(new ValidationFault { Message = reason });
            }

            if (sample.FrequencyHz <= 0 || sample.R_ohm <= 0)
            {
                const string reason = "Frekvencija i R_ohm moraju biti veći od 0.";
                LogReject(reason, sample);
                throw new FaultException<ValidationFault>(new ValidationFault { Message = reason });
            }

            if (sample.T_degC < -50 || sample.T_degC > 100)
            {
                const string reason = "Temperatura mora biti u opsegu -50 do 100.";
                LogReject(reason, sample);
                throw new FaultException<ValidationFault>(new ValidationFault { Message = reason });
            }

            if (_currentMeta.TotalRows > 0 && _acceptedSamples >= _currentMeta.TotalRows)
            {
                string reason = $"Primljen je veći broj uzoraka od prijavljenih ({_currentMeta.TotalRows}).";
                LogReject(reason, sample);
                throw new FaultException<ValidationFault>(new ValidationFault { Message = reason });
            }
        }

        private void AppendAcceptedSample(EisSample sample, double phi, double q)
        {
            _sessionWriter.WriteLine(string.Join(",",
                sample.RowIndex,
                sample.FrequencyHz.ToString(CultureInfo.InvariantCulture),
                sample.R_ohm.ToString(CultureInfo.InvariantCulture),
                sample.X_ohm.ToString(CultureInfo.InvariantCulture),
                sample.T_degC.ToString(CultureInfo.InvariantCulture),
                sample.Range_ohm.ToString(CultureInfo.InvariantCulture),
                sample.TimestampLocal.ToString("O", CultureInfo.InvariantCulture),
                phi.ToString(CultureInfo.InvariantCulture),
                q.ToString(CultureInfo.InvariantCulture)));
            _sessionWriter.Flush();
            _lastRowIndex = sample.RowIndex;
        }

        private void CheckPhaseAngleShift(EisSample sample, double phi)
        {
            if (!_lastPhi.HasValue)
            {
                return;
            }

            double deltaPhi = phi - _lastPhi.Value;
            double phiThreshold = ReadDoubleSetting("Phi_threshold");
            if (Math.Abs(deltaPhi) <= phiThreshold)
            {
                return;
            }

            string direction = deltaPhi > 0
                ? ReadStringSetting("PhaseShiftPositiveMessage")
                : ReadStringSetting("PhaseShiftNegativeMessage");
            string message = $"PhaseAngleShift: {direction}; dPhi={deltaPhi:F4} rad, FrequencyHz={sample.FrequencyHz:F4}, SoC={_currentMeta.SoC}.";
            TransferNotification notification = CreateNotification(
                "OnWarningRaised",
                message,
                sample.RowIndex,
                sample.FrequencyHz,
                0,
                deltaPhi);

            RaiseWarning(notification);
        }

        private void CheckReactiveRatioBounds(EisSample sample, double q)
        {
            double qMin = ReadDoubleSetting("Q_min");
            double qMax = ReadDoubleSetting("Q_max");
            if (q >= qMin && q <= qMax)
            {
                return;
            }

            string message = $"ReactiveRatioOutOfBounds: row={sample.RowIndex}, SoC={_currentMeta.SoC}, BatteryId={_currentMeta.BatteryId}, FrequencyHz={sample.FrequencyHz:F4}, Q={q:F4}.";
            TransferNotification notification = CreateNotification(
                "OnWarningRaised",
                message,
                sample.RowIndex,
                sample.FrequencyHz,
                q,
                0);

            RaiseWarning(notification);
            LogReject(ReadStringSetting("ReactiveRatioOutOfBoundsMessage"), sample);
        }

        private void CheckReactiveRatioDeviation(EisSample sample, double q, double baselineQ)
        {
            if (_qCount == 0 || baselineQ <= 0)
            {
                return;
            }

            double deviationPercent = ReadDoubleSetting("Q_deviation_percent");
            double lowerBound = baselineQ * (1.0 - deviationPercent);
            double upperBound = baselineQ * (1.0 + deviationPercent);
            if (q >= lowerBound && q <= upperBound)
            {
                return;
            }

            string direction = q < lowerBound
                ? ReadStringSetting("ReactiveRatioLowMessage")
                : ReadStringSetting("ReactiveRatioHighMessage");
            string message = $"ReactiveRatioWarning: row={sample.RowIndex}, SoC={_currentMeta.SoC}, BatteryId={_currentMeta.BatteryId}, FrequencyHz={sample.FrequencyHz:F4}, trenutniQ={q:F4}, prosecniQ={baselineQ:F4}, smer={direction}.";
            TransferNotification notification = CreateNotification(
                "OnWarningRaised",
                message,
                sample.RowIndex,
                sample.FrequencyHz,
                q,
                0);

            notification.ReferenceQ = baselineQ;
            RaiseWarning(notification);
            LogReject($"Q odstupa vise od 20% od tekuceg proseka ({direction}).", sample);
        }

        private void RaiseTransferStarted(TransferNotification notification)
        {
            OnTransferStarted?.Invoke(notification);
            SendNotification(notification);
        }

        private void RaiseSampleReceived(TransferNotification notification)
        {
            OnSampleReceived?.Invoke(notification);
            SendNotification(notification);
        }

        private void RaiseTransferCompleted(TransferNotification notification)
        {
            OnTransferCompleted?.Invoke(notification);
            SendNotification(notification);
        }

        private void RaiseWarning(TransferNotification notification)
        {
            OnWarningRaised?.Invoke(notification);
            SendNotification(notification);
        }

        private void SendNotification(TransferNotification notification)
        {
            try
            {
                _callback?.ReceiveNotification(notification);
            }
            catch
            {
                // Callback is best-effort; server processing should continue.
            }
        }

        private TransferNotification CreateNotification(string eventType, string message, int rowIndex, double frequencyHz, double q, double deltaPhi)
        {
            return new TransferNotification
            {
                EventType = eventType,
                Message = message,
                BatteryId = _currentMeta?.BatteryId,
                TestId = _currentMeta?.TestId,
                SoC = _currentMeta?.SoC ?? 0,
                RowIndex = rowIndex,
                FrequencyHz = frequencyHz,
                Q = q,
                DeltaPhi = deltaPhi,
                Timestamp = DateTime.Now
            };
        }

        private void EnsureActiveSession()
        {
            if (_currentMeta == null || _sessionWriter == null || _rejectWriter == null)
            {
                throw new InvalidOperationException("Aktivna sesija nije pokrenuta.");
            }
        }

        private void LogReject(string reason, EisSample sample)
        {
            if (_rejectWriter == null)
            {
                return;
            }

            _rejectWriter.WriteLine(string.Join(",",
                DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                EscapeCsv(reason),
                sample?.RowIndex.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample?.FrequencyHz.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample?.R_ohm.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample?.X_ohm.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample?.T_degC.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample?.Range_ohm.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            _rejectWriter.Flush();
        }

        private void CloseSessionFiles()
        {
            _sessionWriter?.Dispose();
            _rejectWriter?.Dispose();
            _sessionWriter = null;
            _rejectWriter = null;
        }

        private void ResetSessionMetrics()
        {
            _currentMeta = null;
            _sessionFilePath = null;
            _rejectsFilePath = null;
            _callback = null;
            _lastRowIndex = -1;
            _acceptedSamples = 0;
            _lastPhi = null;
            _qSum = 0;
            _qCount = 0;
        }

        private static double ReadDoubleSetting(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        private static string ReadStringSetting(string key)
        {
            return ConfigurationManager.AppSettings[key] ?? string.Empty;
        }

        private static string EscapeCsv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
