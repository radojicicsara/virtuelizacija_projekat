using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;
using System.ServiceModel;
using System.IO;

namespace Service
{
    public class BatteryService : IBatteryService, IDisposable
    {
        private StreamWriter _writer;
        private int _lastRowIndex = -1;
        private bool disposed = false;
        private string _currentFileName;
        private static bool _simulacijaUradjena = false;

        public string StartSession(EisMeta meta)
        {
            _currentFileName = meta.FileName;
            _lastRowIndex = -1;
            disposed = false; 
            Console.WriteLine($"[SERVER] Započeta sesija za: {meta.BatteryId}, Test: {meta.TestId}");

            
            string folderPath = Path.Combine("Data", meta.BatteryId, meta.TestId, meta.SoC.ToString());

            Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, "merenja.csv");
            _writer = new StreamWriter(filePath, append: false);

            _writer.WriteLine("RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm,TimestampLocal");
            _writer.Flush();

            return "ACK: Sesija započeta.";
        }

        public void PushSample(EisSample sample)
        {
            try
            {
                if (sample == null)
                {
                    var fault = new DataFormatFault { Message = "Podaci nisu poslati (null)." };
                    throw new FaultException<DataFormatFault>(fault);
                }
                
                if (sample.RowIndex <= _lastRowIndex)
                {
                    var fault = new ValidationFault { Message = $"RowIndex mora monotono rasti. Poslednji je bio {_lastRowIndex}, a stigao je {sample.RowIndex}." };
                    throw new FaultException<ValidationFault>(fault);
                }
                
                if (sample.FrequencyHz <= 0 || sample.R_ohm <= 0)
                {
                    var fault = new ValidationFault { Message = "Frekvencija i otpor moraju biti veći od 0." };
                    throw new FaultException<ValidationFault>(fault);
                }

                if (sample.T_degC < -50 || sample.T_degC > 100)
                {
                    var fault = new ValidationFault { Message = "Temperatura mora biti u opsegu -50 do 100 stepeni." };
                    throw new FaultException<ValidationFault>(fault);
                }


                if (sample.RowIndex == 5 && !_simulacijaUradjena)
                {
                    _simulacijaUradjena = true;
                    Console.WriteLine("[OPORAVAK] Prekinut prenos: Simuliran prekid veze usred prenosa!");
                    Console.WriteLine("[OPORAVAK] Resursi su ipak zatvoreni zahvaljujući using/Dispose.");
                    Dispose();
                    return;
                }

                _lastRowIndex = sample.RowIndex;

                if (_writer != null)
                {
                    _writer.WriteLine($"{sample.RowIndex},{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.T_degC},{sample.Range_ohm},{sample.TimestampLocal}");
                    _writer.Flush();
                }
            }
            catch (FaultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OPORAVAK] Prekinut prenos: {ex.Message}");
                Console.WriteLine($"[OPORAVAK] Resursi su ipak zatvoreni zahvaljujući using/Dispose.");
                Dispose();
                throw;
            }
        }

        public string EndSession()
        {
            Console.WriteLine($"[Dispose] Resursi zatvoreni za: {_currentFileName}");
            Dispose();
            Console.WriteLine("[SERVER] Sesija uspešno završena.");
            return "ACK: Sesija završena.";
        }

        ~BatteryService()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing && _writer != null)
                {
                    _writer.Close();
                    _writer.Dispose();
                    _writer = null;
                }
                disposed = true;
            }
        }
    }
}