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
    // Ključni deo: ": IBatteryService" znači da ova klasa realizuje tvoj ugovor
    public class BatteryService : IBatteryService, IDisposable
    {

        private StreamWriter _writer;
        private int _lastRowIndex = -1; // Dodajemo ovo da pratimo monotoni rast

        public void StartSession(EisMeta meta)
        {
            _lastRowIndex = -1; // Resetujemo brojač na početku svake sesije
            Console.WriteLine($"[SERVER] Započeta sesija za: {meta.BatteryId}, Test: {meta.TestId}");
            // Ovde ćemo kasnije dodati pravljenje foldera
        }

        public void PushSample(EisSample sample)
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

            // Ako je sve u redu, ažuriramo poslednji indeks i pišemo u fajl
            _lastRowIndex = sample.RowIndex;

            // Ako je sve OK, upiši u fajl
            if (_writer != null)
            {
                _writer.WriteLine($"{sample.RowIndex},{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.T_degC},{sample.Range_ohm},{sample.TimestampLocal}");
                _writer.Flush();
            }
        }

      
        public void EndSession()
        {
            Dispose(); // Zatvaramo resurse kada klijent završi
            Console.WriteLine("[SERVER] Sesija uspešno završena.");
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
        }

    }
}