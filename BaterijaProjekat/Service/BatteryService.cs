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

        public void StartSession(EisMeta meta)
        {
            _currentFileName = meta.FileName;
            _lastRowIndex = -1;
            disposed = false; // resetujemo za novu sesiju
            Console.WriteLine($"[SERVER] Započeta sesija za: {meta.BatteryId}, Test: {meta.TestId}");

            // 1. PRAVLJENJE PUTANJE (Zadatak 1 i 5)
            string folderPath = Path.Combine("Data", meta.BatteryId, meta.TestId, meta.SoC.ToString());

            // 2. KREIRANJE FOLDERA
            Directory.CreateDirectory(folderPath);

            // 3. OTVARANJE FAJLA ZA PISANJE
            string filePath = Path.Combine(folderPath, "merenja.csv");
            _writer = new StreamWriter(filePath, append: false);

            // 4. UPISIVANJE ZAGLAVLJA (Header)
            _writer.WriteLine("RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm,TimestampLocal");
            _writer.Flush();
        }

        public void PushSample(EisSample sample)
        {
            try
            {
                // 1. Provera prisutnih polja (Zadatak 3)
                if (sample == null)
                {
                    var fault = new DataFormatFault { Message = "Podaci nisu poslati (null)." };
                    throw new FaultException<DataFormatFault>(fault);
                }
                // 2. Provera monotonog rasta RowIndex-a
                if (sample.RowIndex <= _lastRowIndex)
                {
                    var fault = new ValidationFault { Message = $"RowIndex mora monotono rasti. Poslednji je bio {_lastRowIndex}, a stigao je {sample.RowIndex}." };
                    throw new FaultException<ValidationFault>(fault);
                }
                // 3. Ostale validacije iz zadatka
                if (sample.FrequencyHz <= 0 || sample.R_ohm <= 0)
                {
                    var fault = new ValidationFault { Message = "Frekvencija i otpor moraju biti veći od 0." };
                    throw new FaultException<ValidationFault>(fault);
                }

                // 4. Simulacija prekida veze usred prenosa (Zadatak 4)
                if (sample.RowIndex == 5 && !_simulacijaUradjena)
                {
                    _simulacijaUradjena = true;
                    Console.WriteLine("[OPORAVAK] Prekinut prenos: Simuliran prekid veze usred prenosa!");
                    Console.WriteLine("[OPORAVAK] Resursi su ipak zatvoreni zahvaljujući using/Dispose.");
                    Dispose();
                    return;
                }

                // Ako je sve u redu, ažuriramo poslednji indeks i pišemo u fajl
                _lastRowIndex = sample.RowIndex;

                if (_writer != null)
                {
                    _writer.WriteLine($"{sample.RowIndex},{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.T_degC},{sample.Range_ohm},{sample.TimestampLocal}");
                    _writer.Flush();
                    //Console.WriteLine($"[SERVER] Primljen uzorak #{sample.RowIndex} | F={sample.FrequencyHz}Hz | R={sample.R_ohm}Ω");
                }
            }
            catch (FaultException)
            {
                // FaultException prosleđujemo klijentu, ne obrađujemo ovde
                throw;
            }
            catch (Exception ex)
            {
                // 4. Oporavak u slučaju prekida veze (Zadatak 4)
                Console.WriteLine($"[OPORAVAK] Prekinut prenos: {ex.Message}");
                Console.WriteLine($"[OPORAVAK] Resursi su ipak zatvoreni zahvaljujući using/Dispose.");
                Dispose();
                throw;
            }
        }

        public void EndSession()
        {
            Console.WriteLine($"[Dispose] Resursi zatvoreni za: {_currentFileName}");
            Dispose();
            Console.WriteLine("[SERVER] Sesija uspešno završena.");
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